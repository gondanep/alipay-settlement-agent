using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Settlement.Core.Configuration;

namespace Settlement.Agent;

public sealed class SettlementWorker(SettlementRunner runner, SettlementOptions options, ILogger<SettlementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Sfg.PollIntervalSeconds));
        logger.LogInformation("Watching SFG ({Mode}) every {Interval}", options.Sfg.Mode, interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                runner.RunOnce();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Settlement poll failed; retrying in {Interval}", interval);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
