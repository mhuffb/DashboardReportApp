using DashboardReportApp.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

public class EmailProcessingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;

    public EmailProcessingBackgroundService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var emailService = scope.ServiceProvider.GetRequiredService<EmailAttachmentService>();

                try
                {
                    await emailService.ProcessIncomingEmailsAsync();
                }
                catch (Exception ex)
                {
                    // Handle exceptions (log them, notify admin, etc.)
                    Console.WriteLine($"Error processing emails: {ex.Message}");
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
