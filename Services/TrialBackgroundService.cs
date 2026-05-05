using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

public class TrialBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TrialBackgroundService> _logger;

    public TrialBackgroundService(IServiceProvider serviceProvider, ILogger<TrialBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var trialService = scope.ServiceProvider.GetRequiredService<TrialService>();

            try
            {
                await trialService.ProcessTrialsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trial job failed");
            }

            // רץ כל 24 שעות
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
