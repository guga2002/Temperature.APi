using Common.BotNatia.Interfaces;
using Common.BotNatia.MesageSender;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Buffers;

namespace Common.BotNatia.Job;

public class ChanellChecker: BackgroundService
{
    private readonly BootSendInfo _telegramService;
    private readonly ILogger<ChanellChecker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryCache _cach;

    public ChanellChecker(BootSendInfo telegramService, ILogger<ChanellChecker> logger, IServiceProvider serviceProvider, IMemoryCache cach)
    {
        _telegramService = telegramService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _cach=cach;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("✅ Natia background monitoring started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var scope = _serviceProvider.CreateScope();

            var chanellService = scope.ServiceProvider.GetRequiredService<IChanellServices>();
            try
            {
                var ports=await chanellService.GetPortsWhereAlarmsIsOn();

                var chanells = await chanellService.GetChannelsByPortIn250ListAsync(ports);
                await CheckForProblemAsync(ports, chanells);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NatiaAlertWorker.");
            }

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }

    private async Task CheckForProblemAsync(List<int> ports, List<string> chanells)
    {
        await SendHealthyDayReportIfNoOutageAsync();
        await Console.Out.WriteLineAsync("natia watchout");
        const string lastAlertKey = "LastSmsSentToGlobal";
        const string lastCountKey = "LastChanellCounts";

        var currentCount = ports.Count;
        var previousCount = _cach.TryGetValue(lastCountKey, out int prev) ? prev : 0;

        _cach.Set(lastCountKey, currentCount, TimeSpan.FromMinutes(15));

        if (previousCount >= 5 && currentCount <5)
        {
            await _telegramService.SentMessageToTelegram("✅ ყველა არხი ჩაირთო. ბოდიშით შეფერხებისთვის.");
            _cach.Remove(lastAlertKey);
            return;
        }

        if (currentCount < 5)
        {
            return;
        }

        if (currentCount >= 5)
        {
            _cach.Set("ChannelOutageDetectedInLast24h", true, TimeSpan.FromHours(24));
        }


        if (!_cach.TryGetValue(lastAlertKey, out _))
        {
            string channelList = string.Empty;

            if (chanells is { Count: > 0 })
            {
                channelList = "📺 გათიშული არხები:\n";

                foreach (var name in chanells)
                {
                    channelList += $"- {name}\n";
                }
            }

            var message = $"📡 გამარჯობა,\n" +
                          $"დაფიქსირდა ტექნიკური შეფერხება ან უამინდობა, რის გამოც რამდენიმე ტელეარხი დროებით არ მაუწყებლობს.\n\n" +
                          $"🔴 გათიშულია {currentCount} არხი.\n" +
                          $"{channelList}" +
                          $"⏰ დრო: {DateTime.Now:HH:mm:ss}\n\n" +
                          $"🙏 ჩვენი ტექნიკური ჯგუფი უკვე მუშაობს პრობლემაზე.\n" +
                          $"გმადლობთ გაგებისთვის და მხარდაჭერისთვის.";

            await _telegramService.SentMessageToTelegram(message);
            _cach.Set(lastAlertKey, true, TimeSpan.FromMinutes(30));
        }
    }

    private async Task SendHealthyDayReportIfNoOutageAsync()
    {
        if (DateTime.Now.Hour != 23 || DateTime.Now.Minute < 59)
            return;

        const string lastDailyReportKey = "HealthyReportLastSent";
        const string outageFlagKey = "ChannelOutageDetectedInLast24h";


        if (_cach.TryGetValue(lastDailyReportKey, out _))
            return;

        if (_cach.TryGetValue(outageFlagKey, out _))
            return;

        var message = $"🟢 *ყველაფერი რიგზეა!*\n" +
                      $"ბოლო 24 საათის განმავლობაში არხების გათიშვა არ დაფიქსირებულა.\n" +
                      $"⏰ დრო: {DateTime.Now:HH:mm:ss}\n\n" +
                      $"ნატია აგრძელებს მონიტორინგს. 🛰️";

        await _telegramService.SentMessageToTelegram(message);

        _cach.Set(lastDailyReportKey, true, TimeSpan.FromHours(24));
    }


}
