using Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Background;

public sealed class FacilitatorElectionWorker(
    IServiceScopeFactory scopes,
    ILogger<FacilitatorElectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recovered = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var elections = scope.ServiceProvider.GetRequiredService<FacilitatorElectionService>();
                if (!recovered)
                {
                    await elections.RecoverAsync(stoppingToken);
                    recovered = true;
                }

                await elections.ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                logger.LogWarning("Facilitator election pass failed.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
        }
    }
}
