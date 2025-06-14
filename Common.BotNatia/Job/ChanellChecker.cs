using Common.BotNatia.Interfaces;
using Common.BotNatia.MesageSender;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot;
using System.IO;

namespace Common.BotNatia.Job;

public class ChanellChecker : BackgroundService
{
    private readonly BootSendInfo _telegramService;
    private readonly ILogger<ChanellChecker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryCache _cache;
    private readonly TelegramBotClient _botClient;

    private const string LastAlertKey = "LastSmsSentToGlobal";
    private const string LastCountKey = "LastChanellCounts";
    private const string OutageFlagKey = "ChannelOutageDetectedInLast24h";
    private const string LastDailyReportKey = "HealthyReportLastSent";
    private const long ChatId = -1002573516355;

    public ChanellChecker(BootSendInfo telegramService, ILogger<ChanellChecker> logger, IServiceProvider serviceProvider, IMemoryCache cache)
    {
        _telegramService = telegramService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _cache = cache;
        _botClient = new TelegramBotClient("7992931942:AAHAfog7gNKm1yaAoNe4FZeEhdjmet2Zi7U");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("✅ Natia background monitoring started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var chanellService = scope.ServiceProvider.GetRequiredService<IChanellServices>();

                var ports = await chanellService.GetPortsWhereAlarmsIsOn();
                var chanells = await chanellService.GetChannelsByPortIn250ListAsync(ports);

                await CheckForProblemAsync(ports, chanells, chanellService);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NatiaAlertWorker.");
            }

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }

    private async Task CheckForProblemAsync(List<int> ports, List<string> chanells, IChanellServices chanellService)
    {
        await SendHealthyDayReportIfNoOutageAsync(chanellService);
        await Console.Out.WriteLineAsync("natia watchout");

        int currentCount = ports.Count;
        int previousCount = _cache.TryGetValue(LastCountKey, out int prev) ? prev : 0;
        _cache.Set(LastCountKey, currentCount, TimeSpan.FromMinutes(15));

        if (previousCount >= 5 && currentCount < 5)
        {
            await SendVoiceAsync(chanellService, "ყველა არხი ჩაირთო. ბოდიშით შეფერხებისთვის.");
            await _telegramService.SentMessageToTelegram("✅ ყველა არხი ჩაირთო. ბოდიშით შეფერხებისთვის.");
            _cache.Remove(LastAlertKey);
            return;
        }

        if (currentCount < 5) return;

        _cache.Set(OutageFlagKey, true, TimeSpan.FromHours(24));

        if (!_cache.TryGetValue(LastAlertKey, out _))
        {
            await SendVoiceAsync(chanellService, "გამარჯობა, არხები გაგვეთიშა, გადაამოწმეთ ჩემი შეტყობინება.");

            string channelList = chanells is { Count: > 0 }
                ? "📺 გათიშული არხები:\n" + string.Join("\n", chanells.Select(c => $"- {c}"))
                : string.Empty;

            string message = $"📡 გამარჯობა,\n" +
                             $"დაფიქსირდა ტექნიკური შეფერხება ან უამინდობა, რის გამოც რამდენიმე ტელეარხი დროებით არ მაუწყებლობს.\n\n" +
                             $"🔴 გათიშულია {currentCount} არხი.\n" +
                             $"{channelList}\n" +
                             $"⏰ დრო: {DateTime.Now:HH:mm:ss}\n\n" +
                             $"🙏 ჩვენი ტექნიკური ჯგუფი უკვე მუშაობს პრობლემაზე.\n" +
                             $"გმადლობთ გაგებისთვის და მხარდაჭერისთვის.";

            await _telegramService.SentMessageToTelegram(message);
            _cache.Set(LastAlertKey, true, TimeSpan.FromMinutes(30));
        }
    }

    private async Task SendHealthyDayReportIfNoOutageAsync(IChanellServices chanellService)
    {
        if (DateTime.Now.Hour != 23 || DateTime.Now.Minute < 59) return;
        if (_cache.TryGetValue(LastDailyReportKey, out _)) return;
        if (_cache.TryGetValue(OutageFlagKey, out _)) return;

        await SendVoiceAsync(chanellService, "ყველაფერი რიგზეა! ბოლო 24 საათის განმავლობაში არხების გათიშვა არ დაფიქსირებულა");
        _cache.Set(LastDailyReportKey, true, TimeSpan.FromHours(24));
    }

    private async Task SendVoiceAsync(IChanellServices chanellService, string text)
    {
        try
        {
            var wavBytes = await chanellService.SpeakNow(text);
            using var oggStream = await chanellService.ConvertWavToOggStreamAsync(wavBytes);

            var memoryStream = new MemoryStream();
            await oggStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            await _botClient.SendVoiceAsync(
                ChatId,
                voice: new InputOnlineFile(memoryStream, "NatiaVoice.ogg"),
                caption: "🔊 Alert from Natia"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send voice message.");
        }
    }
}
