using TraccarAgent.Models;
using TraccarAgent.Repositories;

namespace TraccarAgent.Services;

public class PositionWorker : BackgroundService
{
    private readonly PersistentQueue _queue;
    private readonly PositionProcessor _processor;
    private readonly int _batchSize;
    private readonly ILogger<PositionWorker> _logger;

    public PositionWorker(
        PersistentQueue queue,
        PositionProcessor processor,
        IConfiguration config,
        ILogger<PositionWorker> logger)
    {
        _queue = queue;
        _processor = processor;
        _batchSize = config.GetValue<int>("Queue:BatchSize", 100);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PositionWorker iniciado (lotes de {BatchSize}).", _batchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await _queue.DequeueBatchAsync(_batchSize, stoppingToken);

                if (batch.Count == 0) continue;

                try
                {
                    await _processor.ProcessBatchAsync(batch.Select(b => b.Dto).ToList());
                    await _queue.MarkBatchDoneAsync(batch.Select(b => b.Id));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fallo al procesar lote de {Count} posiciones, se reintentará.", batch.Count);
                    await _queue.MarkBatchFailedAsync(batch.Select(b => b.Id));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en PositionWorker, reintentando en 2s.");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
