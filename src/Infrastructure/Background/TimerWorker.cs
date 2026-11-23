using Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Background;

public sealed class TimerWorker(
    IServiceScopeFactory scopes,
    IMeetingTimer timer,
    TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var expired = timer.DrainExpired(timeProvider.GetUtcNow());
            if (expired.Count > 0)
            {
                using var scope = scopes.CreateScope();
                var notifier = scope.ServiceProvider.GetRequiredService<IMeetingNotifier>();
                foreach (var notification in expired)
                {
                    await notifier.TimerExpiredAsync(
                        notification.MeetingId,
                        notification.RunId,
                        notification.Phase,
                        stoppingToken);
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), stoppingToken);
        }
    }
}
