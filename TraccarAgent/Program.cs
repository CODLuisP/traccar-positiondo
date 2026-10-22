using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraccarAgent.Models;
using TraccarAgent.Repositories;
using TraccarAgent.Services;

// ─── Serilog ──────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .MinimumLevel.Override("TraccarAgent", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting", Serilog.Events.LogEventLevel.Information)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Iniciando TraccarAgent...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    builder.Services.AddSingleton<PersistentQueue>();
    builder.Services.AddSingleton<PositionProcessor>();

    var workerCount = builder.Configuration.GetValue<int>("Queue:Workers", 10);
    for (int i = 0; i < workerCount; i++)
        builder.Services.AddHostedService<PositionWorker>();

    builder.Services.ConfigureHttpJsonOptions(opts =>
    {
        opts.SerializerOptions.PropertyNameCaseInsensitive = true;
        opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

    var app = builder.Build();

    // ─── WEBHOOK ──────────────────────────────────────────────────────────────
    app.MapPost("/webhook/traccar", async (
        HttpRequest request,
        PersistentQueue queue,
        IConfiguration config,
        HttpContext ctx) =>
    {
        var secretToken = config["Webhook:SecretToken"];
        if (!string.IsNullOrEmpty(secretToken))
        {
            var receivedToken = ctx.Request.Headers["Authorization"].ToString();
            if (receivedToken != $"Bearer {secretToken}")
                return Results.Unauthorized();
        }

        try
        {
            var body = await new StreamReader(request.Body).ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(body))
                return Results.Ok();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (body.TrimStart().StartsWith("["))
            {
                var list = JsonSerializer.Deserialize<List<TraccarWebhookDto>>(body, options) ?? [];
                foreach (var webhook in list)
                    await queue.EnqueueAsync(webhook);
            }
            else
            {
                var single = JsonSerializer.Deserialize<TraccarWebhookDto>(body, options);
                if (single is not null)
                    await queue.EnqueueAsync(single);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error procesando webhook, payload descartado");
            return Results.Problem(statusCode: 500);
        }

        return Results.Ok();
    });

    app.MapGet("/webhook/traccar", () => Results.Ok());

    // ─── PAUSA / REANUDA ──────────────────────────────────────────────────────
    app.MapPost("/queue/pause", (PersistentQueue queue) =>
    {
        queue.Pause();
        return Results.Ok(new { status = "paused", message = "Workers detenidos. Webhooks se acumulan en SQLite." });
    });

    app.MapPost("/queue/resume", (PersistentQueue queue) =>
    {
        queue.Resume();
        return Results.Ok(new { status = "resumed", message = "Workers activos. Procesando posiciones acumuladas." });
    });

    app.MapGet("/queue/status", (PersistentQueue queue) =>
        Results.Ok(new { paused = queue.IsPaused, timestamp = DateTimeOffset.UtcNow }));

    // ─── HEALTH CHECK ─────────────────────────────────────────────────────────
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "ok",
        timestamp = DateTimeOffset.UtcNow
    }));

    Log.Information("TraccarAgent listo");
    Log.Information("  Webhook : POST http://TU_IP:5008/webhook/traccar");
    Log.Information("  Health  : GET  http://TU_IP:5008/health");
    Log.Information("  Pausar  : POST http://TU_IP:5008/queue/pause");
    Log.Information("  Reanudar: POST http://TU_IP:5008/queue/resume");
    Log.Information("  Estado  : GET  http://TU_IP:5008/queue/status");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "TraccarAgent terminó inesperadamente");
}
finally
{
    Log.CloseAndFlush();
}
