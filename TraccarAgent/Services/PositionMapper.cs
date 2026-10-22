using System.Text.Json;
using TraccarAgent.Models;

namespace TraccarAgent.Services;

/// <summary>
/// Centraliza todas las conversiones de unidades y lecturas de atributos.
/// Traccar usa nudos, metros, milisegundos → nosotros usamos km/h, km, horas.
/// </summary>
public static class PositionMapper
{
    // Conversión: nudos → km/h
    public static double? SpeedKph(double? speedKnots) =>
        speedKnots.HasValue ? Math.Round(speedKnots.Value * 1.852, 2) : null;

    // Conversión: metros → km
    public static double? MetersToKm(double? meters) =>
        meters.HasValue ? Math.Round(meters.Value / 1000.0, 3) : null;

    // Conversión: milisegundos → horas motor
    public static double? MsToHours(double? ms) =>
        ms.HasValue ? Math.Round(ms.Value / 3600000.0, 2) : null;

    // Conversión: mV → voltios (Teltonika)
    public static double? MvToVolts(double? mv) =>
        mv.HasValue ? Math.Round(mv.Value / 1000.0, 3) : null;

    // Conversión: DateTime UTC → Unix timestamp (int)
    public static long? ToUnixTimestamp(DateTime? dt) =>
        dt.HasValue ? ((DateTimeOffset)dt.Value.ToUniversalTime()).ToUnixTimeSeconds() : null;

    // Conversión: epoch en milisegundos (motionTime de Traccar) → Unix timestamp en segundos
    public static long? MsEpochToUnixSeconds(long? epochMs) =>
        epochMs.HasValue ? epochMs.Value / 1000 : null;

    // Leer atributo del diccionario con seguridad
    public static T? GetAttr<T>(Dictionary<string, object>? attrs, string key) where T : struct
    {
        if (attrs is null || !attrs.TryGetValue(key, out var val) || val is null)
            return null;
        try
        {
            if (val is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.Number => (T)Convert.ChangeType(je.GetDouble(), typeof(T)),
                    JsonValueKind.True => (T)Convert.ChangeType(true, typeof(T)),
                    JsonValueKind.False => (T)Convert.ChangeType(false, typeof(T)),
                    JsonValueKind.String => (T)Convert.ChangeType(je.GetString()!, typeof(T)),
                    _ => null
                };
            }
            return (T)Convert.ChangeType(val, typeof(T));
        }
        catch { return null; }
    }

    public static string? GetAttrString(Dictionary<string, object>? attrs, string key)
    {
        if (attrs is null || !attrs.TryGetValue(key, out var val) || val is null)
            return null;

        if (val is JsonElement je)
            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.GetRawText();

        return val.ToString();
    }

    public static double? GetOdometerKm(Dictionary<string, object>? attrs, string? protocol)
    {
        // "totalDistance" en metros (todos los protocolos)
        var totalDistance = GetAttr<double>(attrs, "totalDistance");
        if (totalDistance.HasValue) return MetersToKm(totalDistance);

        // Teltonika: fallback a "odometer" en metros
        if (protocol == "teltonika")
            return MetersToKm(GetAttr<double>(attrs, "odometer"));

        return null;
    }

    // Compacta la celda GSM/LTE servidora en "mcc-mnc-lac-cellId"
    public static string? FormatCellInfo(TraccarNetworkDto? network)
    {
        var cell = network?.CellTowers?.FirstOrDefault();
        if (cell is null) return null;
        return $"{cell.MobileCountryCode}-{cell.MobileNetworkCode}-{cell.LocationAreaCode}-{cell.CellId}";
    }

    // Construye los parámetros para UPDATE device
    public static DeviceUpdateParams BuildDeviceUpdate(TraccarPositionDto pos, DeviceInfo device)
    {
        var attrs = pos.Attributes;
        bool? ignition = GetAttr<bool>(attrs, "ignition");
        bool? motion = GetAttr<bool>(attrs, "motion");
        long? fixTimestamp = ToUnixTimestamp(pos.FixTime);
        long? serverTimestamp = ToUnixTimestamp(pos.ServerTime);
        long? deviceTimestamp = ToUnixTimestamp(pos.DeviceTime);

        return new DeviceUpdateParams
        {
            AccountId = device.AccountId,
            DeviceId = device.DeviceId,
            LastValidLatitude = pos.Latitude,
            LastValidLongitude = pos.Longitude,
            LastValidHeading = pos.Course,
            LastValidSpeed = SpeedKph(pos.Speed),
            LastGPSTimestamp = fixTimestamp,
            LastEventTimestamp = serverTimestamp,
            LastUpdateTime = serverTimestamp,
            DeviceTime = deviceTimestamp,
            LastOdometerKM = GetOdometerKm(attrs, pos.Protocol),
            LastEngineHours = MsToHours(GetAttr<double>(attrs, "hours")),
            LastBatteryLevel = GetAttr<double>(attrs, "batteryLevel"),
            LastInputState = ignition.HasValue ? (ignition.Value ? 1 : 0) : (int?)null,
            LastIgnitionOnTime = (ignition == true) ? fixTimestamp : null,
            LastIgnitionOffTime = (ignition == false) ? fixTimestamp : null,
            LastAddress = pos.Address,
            Protocol = pos.Protocol,
            LastAccuracy = pos.Accuracy,
            Satellites = GetAttr<int>(attrs, "sat"),
            Rssi = GetAttr<int>(attrs, "rssi"),
            MotionState = motion.HasValue ? (motion.Value ? 1 : 0) : (int?)null,
            ValidFix = pos.Valid.HasValue ? (pos.Valid.Value ? 1 : 0) : (int?)null,
            LastGeozoneID = pos.GeofenceIds is { Count: > 0 } ? string.Join(",", pos.GeofenceIds) : null,
            LastCellServingInfo = FormatCellInfo(pos.Network),
            Status = device.Status,
            CalendarId = device.CalendarId,
            LastPositionId = device.PositionId,
            MotionTime = MsEpochToUnixSeconds(device.MotionTimeMs),
            MotionLat = device.MotionLat,
            MotionLon = device.MotionLon,
        };
    }

    // Construye los parámetros para INSERT eventdata
    public static EventDataInsertParams BuildEventData(TraccarPositionDto pos, DeviceInfo device)
    {
        var attrs = pos.Attributes;

        return new EventDataInsertParams
        {
            AccountId = device.AccountId,
            DeviceId = device.DeviceId,
            Timestamp = (int)(ToUnixTimestamp(pos.FixTime) ?? 0),
            StatusCode = 0,   // se mapeará correctamente en el futuro
            Latitude = pos.Latitude,
            Longitude = pos.Longitude,
            SpeedKPH = SpeedKph(pos.Speed),
            Heading = pos.Course,
            Address = pos.Address,
            Altitude = pos.Altitude,
            DistanceKM = MetersToKm(GetAttr<double>(attrs, "distance")),
            OdometerKM = GetOdometerKm(attrs, pos.Protocol),
            SatelliteCount = GetAttr<int>(attrs, "sat"),
            BatteryLevel = GetAttr<double>(attrs, "batteryLevel"),
            BatteryVolts = MvToVolts(GetAttr<double>(attrs, "battery")),   // Teltonika
            VBatteryVolts = MvToVolts(GetAttr<double>(attrs, "power")),      // Teltonika voltaje externo
            EngineHours = MsToHours(GetAttr<double>(attrs, "hours")),
            SignalStrength = GetAttr<double>(attrs, "rssi"),
            GpsFixType = (pos.Valid == true) ? (short)1 : (short)0,
            CreationTime = (int)(ToUnixTimestamp(pos.ServerTime) ?? 0),
        };
    }
}

// Parámetros tipados para UPDATE device
public class DeviceUpdateParams
{
    public string AccountId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public double? LastValidLatitude { get; set; }
    public double? LastValidLongitude { get; set; }
    public double? LastValidHeading { get; set; }
    public double? LastValidSpeed { get; set; }
    public long? LastGPSTimestamp { get; set; }
    public long? LastEventTimestamp { get; set; }
    public string? LastAddress { get; set; }
    public long? LastUpdateTime { get; set; }
    public long? DeviceTime { get; set; }
    public double? LastOdometerKM { get; set; }
    public double? LastEngineHours { get; set; }
    public double? LastBatteryLevel { get; set; }
    public int? LastInputState { get; set; }
    public long? LastIgnitionOnTime { get; set; }
    public long? LastIgnitionOffTime { get; set; }
    public string? Protocol { get; set; }
    public double? LastAccuracy { get; set; }
    public int? Satellites { get; set; }
    public int? Rssi { get; set; }
    public int? MotionState { get; set; }
    public int? ValidFix { get; set; }
    public string? LastGeozoneID { get; set; }
    public string? LastCellServingInfo { get; set; }
    public string? Status { get; set; }
    public int? CalendarId { get; set; }
    public int? LastPositionId { get; set; }
    public long? MotionTime { get; set; }
    public double? MotionLat { get; set; }
    public double? MotionLon { get; set; }
}

// Parámetros tipados para INSERT eventdata
public class EventDataInsertParams
{
    public string AccountId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public int Timestamp { get; set; }
    public int StatusCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? SpeedKPH { get; set; }
    public double? Heading { get; set; }
    public string? Address { get; set; }
    public double? Altitude { get; set; }
    public double? DistanceKM { get; set; }
    public double? OdometerKM { get; set; }
    public int? SatelliteCount { get; set; }
    public double? BatteryLevel { get; set; }
    public double? BatteryVolts { get; set; }
    public double? VBatteryVolts { get; set; }
    public double? EngineHours { get; set; }
    public double? SignalStrength { get; set; }
    public short GpsFixType { get; set; }
    public int CreationTime { get; set; }
}