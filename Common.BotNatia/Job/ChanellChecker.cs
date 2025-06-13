using Common.BotNatia.MesageSender;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.BotNatia.Job;

internal class ChanellChecker: BackgroundService
{
    private readonly BootSendInfo _telegramService;
    private readonly ILogger<ChanellChecker> _logger;

    public ChanellChecker(BootSendInfo telegramService, ILogger<ChanellChecker> logger)
    {
        _telegramService = telegramService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("✅ Natia background monitoring started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Replace this with your actual condition check
                var issueDetected = await CheckForProblemAsync();

                if (issueDetected)
                {
                    await _telegramService.SendAlertAsync("🚨 *Channel offline or error detected!*", stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NatiaAlertWorker.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // adjust interval
        }
    }

    private async Task<bool> CheckForProblemAsync()
    {
        // 🔍 Example logic: check Redis/SignalR/Database/etc
        return await Task.FromResult(false); // return true if something is wrong
    }
}
