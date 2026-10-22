using Microsoft.Data.Sqlite;
using System.Text.Json;
using TraccarAgent.Models;

namespace TraccarAgent.Services;

public sealed class PersistentQueue : IDisposable
{
    private readonly string _connStr;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private readonly ILogger<PersistentQueue> _logger;
    private volatile bool _paused = false;

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public bool IsPaused => _paused;

    public PersistentQueue(IConfiguration config, ILogger<PersistentQueue> logger)
    {
        _logger = logger;
        var dbPath = config["Queue:DbPath"] ?? "queue.db";
        _connStr = $"Data Source={dbPath}";
        InitDb();
        RecoverOnStartup();
    }

    public void Pause()
    {
        _paused = true;
        _logger.LogWarning("Cola PAUSADA — webhooks se acumulan en SQLite, no se insertan en DB.");
    }

    public void Resume()
    {
        _paused = false;
        _logger.LogWarning("Cola REANUDADA — procesando posiciones pendientes.");
        _signal.Release(10);
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connStr);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=15000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void InitDb()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS queue (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                payload    TEXT    NOT NULL,
                created_at INTEGER NOT NULL,
                status     TEXT    NOT NULL DEFAULT 'pending',
                attempts   INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_queue_status ON queue(status, id);";
        cmd.ExecuteNonQuery();
    }

    private void RecoverOnStartup()
    {
        using var conn = OpenConnection();

        using var recoverCmd = conn.CreateCommand();
        recoverCmd.CommandText = "UPDATE queue SET status='pending' WHERE status='processing'";
        var recovered = recoverCmd.ExecuteNonQuery();
        if (recovered > 0)
            _logger.LogWarning("Recuperados {Count} items del cierre anterior.", recovered);

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM queue WHERE status='pending'";
        var pending = (long)(countCmd.ExecuteScalar() ?? 0L);
        if (pending > 0)
        {
            _logger.LogInformation("{Count} posiciones pendientes en cola al iniciar.", pending);
            _signal.Release(10); // despertar todos los workers
        }
    }

    public async Task EnqueueAsync(TraccarWebhookDto dto)
    {
        var payload = JsonSerializer.Serialize(dto);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await _dbLock.WaitAsync();
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO queue (payload, created_at) VALUES (@p, @t)";
            cmd.Parameters.AddWithValue("@p", payload);
            cmd.Parameters.AddWithValue("@t", now);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _dbLock.Release();
        }

        _signal.Release();
    }

    /// <summary>
    /// Intenta obtener un lote de items pendientes de forma atómica.
    /// Primero intenta sin esperar; si no hay nada, espera señal hasta 5 segundos.
    /// </summary>
    public async Task<List<(long Id, TraccarWebhookDto Dto)>> DequeueBatchAsync(int batchSize, CancellationToken ct)
    {
        if (_paused)
        {
            await _signal.WaitAsync(TimeSpan.FromSeconds(5), ct);
            return [];
        }

        // Intentar inmediatamente primero (para drenar el backlog sin esperar señales)
        var batch = await ClaimBatchAsync(batchSize);
        if (batch.Count > 0) return batch;

        // Si no hay nada, esperar señal
        await _signal.WaitAsync(TimeSpan.FromSeconds(5), ct);
        if (_paused) return [];

        return await ClaimBatchAsync(batchSize);
    }

    private async Task<List<(long Id, TraccarWebhookDto Dto)>> ClaimBatchAsync(int batchSize)
    {
        await _dbLock.WaitAsync();
        try
        {
            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();

            using var selectCmd = conn.CreateCommand();
            selectCmd.Transaction = tx;
            selectCmd.CommandText =
                $"SELECT id, payload FROM queue WHERE status='pending' ORDER BY id LIMIT {batchSize}";

            var ids = new List<long>();
            var payloads = new List<string>();

            using (var reader = selectCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    ids.Add(reader.GetInt64(0));
                    payloads.Add(reader.GetString(1));
                }
            }

            if (ids.Count == 0) { tx.Commit(); return []; }

            var idList = string.Join(",", ids);
            using var claimCmd = conn.CreateCommand();
            claimCmd.Transaction = tx;
            claimCmd.CommandText =
                $"UPDATE queue SET status='processing', attempts=attempts+1 WHERE id IN ({idList}) AND status='pending'";
            claimCmd.ExecuteNonQuery();
            tx.Commit();

            var result = new List<(long, TraccarWebhookDto)>();
            for (int i = 0; i < ids.Count; i++)
            {
                var dto = JsonSerializer.Deserialize<TraccarWebhookDto>(payloads[i], _jsonOpts);
                if (dto is not null) result.Add((ids[i], dto));
            }
            return result;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task MarkBatchDoneAsync(IEnumerable<long> ids)
    {
        var idList = string.Join(",", ids);
        await _dbLock.WaitAsync();
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM queue WHERE id IN ({idList})";
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task MarkBatchFailedAsync(IEnumerable<long> ids)
    {
        var idList = string.Join(",", ids);
        await _dbLock.WaitAsync();
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE queue SET status='pending' WHERE id IN ({idList})";
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _dbLock.Release();
        }
        _signal.Release();
    }

    public void Dispose()
    {
        _signal.Dispose();
        _dbLock.Dispose();
    }
}
