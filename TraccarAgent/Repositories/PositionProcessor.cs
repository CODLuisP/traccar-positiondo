using Dapper;
using MySqlConnector;
using System.Text;
using TraccarAgent.Models;
using TraccarAgent.Services;

namespace TraccarAgent.Repositories;

public class PositionProcessor
{
    private readonly string _connectionStringGestionVilla;
    private readonly string _connectionStringGts;
    private readonly HashSet<string> _gestionVillaAccounts;

    public PositionProcessor(IConfiguration config)
    {
        _connectionStringGestionVilla = config.GetConnectionString("MySql")
            ?? throw new InvalidOperationException("Connection string 'MySql' no encontrada.");
        _connectionStringGts = config.GetConnectionString("MySqlGts")
            ?? throw new InvalidOperationException("Connection string 'MySqlGts' no encontrada.");

        _gestionVillaAccounts = config
            .GetSection("AccountRouting:GestionVillaAccounts")
            .Get<List<string>>()
            ?.Select(a => a.ToLowerInvariant())
            .ToHashSet() ?? [];
    }

    private string GetConnectionString(string accountId) =>
        _gestionVillaAccounts.Contains(accountId.ToLowerInvariant())
            ? _connectionStringGestionVilla
            : _connectionStringGts;

    /// <summary>
    /// Procesa un lote de webhooks:
    /// - INSERT IGNORE bulk en eventdata (todos los items del lote en una sola query)
    /// - UPDATE device solo para el registro más reciente por dispositivo,
    ///   y únicamente si es más nuevo que lo que ya tiene la DB.
    /// </summary>
    public async Task ProcessBatchAsync(List<TraccarWebhookDto> webhooks)
    {
        // Parsear y filtrar items válidos
        var items = new List<(TraccarPositionDto Pos, DeviceInfo Device)>();
        foreach (var webhook in webhooks)
        {
            var pos = webhook.Position;
            var dev = webhook.Device;
            if (pos is null || dev is null) continue;

            var accountId = dev.Attributes?.TryGetValue("accountID", out var acc) == true
                ? acc?.ToString() : null;
            var deviceId = dev.Attributes?.TryGetValue("deviceID", out var did) == true
                ? did?.ToString() : null;

            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(deviceId))
                continue;

            items.Add((pos, new DeviceInfo
            {
                AccountId = accountId,
                DeviceId = deviceId,
                Status = dev.Status,
                CalendarId = dev.CalendarId,
                PositionId = dev.PositionId,
                MotionTimeMs = PositionMapper.GetAttr<long>(dev.Attributes, "motionTime"),
                MotionLat = PositionMapper.GetAttr<double>(dev.Attributes, "motionLat"),
                MotionLon = PositionMapper.GetAttr<double>(dev.Attributes, "motionLon"),
            }));
        }

        if (items.Count == 0) return;

        // Agrupar por base de datos (puede haber dos DBs distintas en el mismo lote)
        var groups = items.GroupBy(x => GetConnectionString(x.Device.AccountId));
        foreach (var group in groups)
            await ProcessGroupAsync(group.Key, group.ToList());
    }

    private async Task ProcessGroupAsync(string connStr, List<(TraccarPositionDto Pos, DeviceInfo Device)> items)
    {
        // Construir parámetros INSERT para todos los items
        var insertParams = items
            .Select(x => PositionMapper.BuildEventData(x.Pos, x.Device))
            .ToList();

        // Quedarse solo con la posición más reciente por dispositivo para el UPDATE
        var latestPerDevice = new Dictionary<string, (DeviceUpdateParams Params, DateTime? FixTime)>();
        foreach (var (pos, device) in items)
        {
            var key = $"{device.AccountId}|{device.DeviceId}";
            if (!latestPerDevice.TryGetValue(key, out var existing) || pos.FixTime > existing.FixTime)
                latestPerDevice[key] = (PositionMapper.BuildDeviceUpdate(pos, device), pos.FixTime);
        }

        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var tx = await conn.BeginTransactionAsync(cts.Token);

        try
        {
            // ── 1. Bulk INSERT en eventdata ───────────────────────────────────
            var (bulkSql, bulkParams) = BuildBulkInsert(insertParams);
            await conn.ExecuteAsync(bulkSql, bulkParams, tx);

            // ── 2. UPDATE device — solo el más reciente, solo si es más nuevo ─
            const string updateSql = @"
                UPDATE device SET
                    lastValidLatitude   = @LastValidLatitude,
                    lastValidLongitude  = @LastValidLongitude,
                    lastValidHeading    = @LastValidHeading,
                    lastValidSpeed      = @LastValidSpeed,
                    lastGPSTimestamp    = @LastGPSTimestamp,
                    lastEventTimestamp  = @LastEventTimestamp,
                    lastUpdateTime      = @LastUpdateTime,
                    deviceTime          = @DeviceTime,
                    lastOdometerKM      = COALESCE(@LastOdometerKM,      lastOdometerKM),
                    lastEngineHours     = COALESCE(@LastEngineHours,     lastEngineHours),
                    lastBatteryLevel    = COALESCE(@LastBatteryLevel,    lastBatteryLevel),
                    lastInputState      = COALESCE(@LastInputState,      lastInputState),
                    lastIgnitionOnTime  = COALESCE(@LastIgnitionOnTime,  lastIgnitionOnTime),
                    lastIgnitionOffTime = COALESCE(@LastIgnitionOffTime, lastIgnitionOffTime),
                    direccion           = @LastAddress,
                    protocol            = COALESCE(@Protocol,            protocol),
                    lastAccuracy        = COALESCE(@LastAccuracy,        lastAccuracy),
                    satellites          = COALESCE(@Satellites,          satellites),
                    rssi                = COALESCE(@Rssi,                rssi),
                    motionState         = COALESCE(@MotionState,         motionState),
                    validFix            = COALESCE(@ValidFix,            validFix),
                    lastGeozoneID       = COALESCE(@LastGeozoneID,       lastGeozoneID),
                    lastCellServingInfo = COALESCE(@LastCellServingInfo, lastCellServingInfo),
                    status              = COALESCE(@Status,              status),
                    calendarId          = COALESCE(@CalendarId,          calendarId),
                    lastPositionId      = COALESCE(@LastPositionId,      lastPositionId),
                    motionTime          = COALESCE(@MotionTime,          motionTime),
                    motionLat           = COALESCE(@MotionLat,           motionLat),
                    motionLon           = COALESCE(@MotionLon,           motionLon)
                WHERE accountID = @AccountId
                  AND deviceID  = @DeviceId
                  AND (lastGPSTimestamp IS NULL OR lastGPSTimestamp < @LastGPSTimestamp)";

            foreach (var (updateParam, _) in latestPerDevice.Values)
                await conn.ExecuteAsync(updateSql, updateParam, tx);

            await tx.CommitAsync();
        }
        catch (Exception)
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static (string Sql, DynamicParameters Params) BuildBulkInsert(List<EventDataInsertParams> items)
    {
        var sb = new StringBuilder(@"INSERT IGNORE INTO eventdata (
            accountID, deviceID, timestamp, statusCode,
            latitude, longitude, speedKPH, heading, altitude,
            distanceKM, odometerKM, satelliteCount,
            batteryLevel, batteryVolts, vBatteryVolts,
            engineHours, signalStrength, gpsFixType, creationTime, address
        ) VALUES ");

        var p = new DynamicParameters();
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"(@a{i},@d{i},@ts{i},@sc{i},@lat{i},@lon{i},@spd{i},@hdg{i},@alt{i}," +
                      $"@dist{i},@odo{i},@sat{i},@bat{i},@bv{i},@vbv{i},@eh{i},@sig{i},@gft{i},@ct{i},@addr{i})");

            var x = items[i];
            p.Add($"a{i}",    x.AccountId);
            p.Add($"d{i}",    x.DeviceId);
            p.Add($"ts{i}",   x.Timestamp);
            p.Add($"sc{i}",   x.StatusCode);
            p.Add($"lat{i}",  x.Latitude);
            p.Add($"lon{i}",  x.Longitude);
            p.Add($"spd{i}",  x.SpeedKPH);
            p.Add($"hdg{i}",  x.Heading);
            p.Add($"alt{i}",  x.Altitude);
            p.Add($"dist{i}", x.DistanceKM);
            p.Add($"odo{i}",  x.OdometerKM);
            p.Add($"sat{i}",  x.SatelliteCount);
            p.Add($"bat{i}",  x.BatteryLevel);
            p.Add($"bv{i}",   x.BatteryVolts);
            p.Add($"vbv{i}",  x.VBatteryVolts);
            p.Add($"eh{i}",   x.EngineHours);
            p.Add($"sig{i}",  x.SignalStrength);
            p.Add($"gft{i}",  x.GpsFixType);
            p.Add($"ct{i}",   x.CreationTime);
            p.Add($"addr{i}", x.Address);
        }

        return (sb.ToString(), p);
    }
}
