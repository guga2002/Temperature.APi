using Common.BotNatia.Interfaces;
using Common.BotNatia.MesageSender;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot;
using System.Text;

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
    private const string MuteAlertsKey = "MuteAlertsUntil";
    private const string OutageHistoryKey = "OutageHistory";
    private const long ChatId = -1002817849163;

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

                await HandleAdminCommandsAsync();

                await CheckForProblemAsync(ports, chanells, chanellService);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NatiaAlertWorker.");
            }

            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
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
            await NotifyRecoveryAsync(chanellService);
            return;
        }

        if (currentCount < 5) return;

        _cache.Set(OutageFlagKey, true, TimeSpan.FromHours(24));
        TrackOutageHistory(chanells);

        if (!_cache.TryGetValue(LastAlertKey, out _) && !IsMuted())
        {
            var severity = GetSeverity(currentCount);
            await SendVoiceAsync(chanellService, GetSeverityVoice(severity));

            var message = BuildAlertMessage(currentCount, chanells, severity);
            await _telegramService.SentMessageToTelegram(message);

            // Optionally: Create and assign tasks in CSI here (placeholder)
            await AutoAssignTasksAsync(chanells, severity);

            // Set cooldown (different for severity)
            _cache.Set(LastAlertKey, true, TimeSpan.FromMinutes(severity == "Critical" ? 10 : 30));
        }
    }

    private async Task NotifyRecoveryAsync(IChanellServices chanellService)
    {
        await SendVoiceAsync(chanellService, "ყველა არხი ჩაირთო. ბოდიშით შეფერხებისთვის.");
        await _telegramService.SentMessageToTelegram("✅ ყველა არხი ჩაირთო. ბოდიშით შეფერხებისთვის.");
        _cache.Remove(LastAlertKey);
    }

    private async Task SendHealthyDayReportIfNoOutageAsync(IChanellServices chanellService)
    {
        if (DateTime.Now.Hour != 23 || DateTime.Now.Minute < 59) return;
        if (_cache.TryGetValue(LastDailyReportKey, out _)) return;
        if (_cache.TryGetValue(OutageFlagKey, out _)) return;

        var report = BuildDailyReport();
        await SendVoiceAsync(chanellService, "ყველაფერი რიგზეა! ბოლო 24 საათის განმავლობაში არხების გათიშვა არ დაფიქსირებულა");
        await _telegramService.SentMessageToTelegram(report);

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

    private bool IsMuted()
    {
        if (_cache.TryGetValue<DateTime>(MuteAlertsKey, out var muteUntil))
        {
            return DateTime.UtcNow < muteUntil;
        }
        return false;
    }

    private string GetSeverity(int channelCount) =>
        channelCount switch
        {
            <= 5 => "Low",
            <= 10 => "Medium",
            _ => "Critical"
        };

    private string GetSeverityVoice(string severity) =>
        severity switch
        {
            "Low" => "მცირე შეფერხება დაფიქსირდა. ტექნიკური ჯგუფი უკვე მუშაობს.",
            "Medium" => "გაფრთხილება: რამდენიმე არხი გათიშულია. ტექნიკური ჯგუფი მუშაობს პრობლემაზე.",
            "Critical" => "სასწრაფო! კრიტიკული გათიშვა. დაუყოვნებლივ გადაამოწმეთ.",
            _ => "გათიშვა დაფიქსირდა."
        };

    private string BuildAlertMessage(int count, List<string> channels, string severity)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"📡 {severity} Alert!");
        sb.AppendLine($"გათიშულია {count} არხი.");
        if (channels?.Any() == true)
        {
            sb.AppendLine("\n📺 არხები:");
            foreach (var c in channels)
                sb.AppendLine($"- {c}");
        }
        sb.AppendLine($"\n⏰ დრო: {DateTime.Now:HH:mm:ss}");
        return sb.ToString();
    }

    private void TrackOutageHistory(List<string> channels)
    {
        var history = _cache.TryGetValue<List<(DateTime, List<string>)>>(OutageHistoryKey, out var h) ? h : new();
        history.Add((DateTime.Now, channels ?? new List<string>()));
        _cache.Set(OutageHistoryKey, history, TimeSpan.FromDays(1));
    }

    private string BuildDailyReport()
    {
        if (!_cache.TryGetValue<List<(DateTime, List<string>)>>(OutageHistoryKey, out var history) || history.Count == 0)
            return "📊 დღიური ანგარიში: გათიშვები არ დაფიქსირებულა.";

        var totalIncidents = history.Count;
        var totalChannels = history.SelectMany(h => h.Item2).GroupBy(c => c).OrderByDescending(g => g.Count()).Take(3);

        var sb = new StringBuilder();
        sb.AppendLine($"📊 დღიური ანგარიში:");
        sb.AppendLine($"- გათიშვები: {totalIncidents}");
        sb.AppendLine($"- დრო: {DateTime.Now:dd MMM yyyy}");
        sb.AppendLine("\nყველაზე ხშირად გათიშული არხები:");
        foreach (var ch in totalChannels)
            sb.AppendLine($"- {ch.Key} ({ch.Count()} შემთხვევა)");
        return sb.ToString();
    }

    private async Task AutoAssignTasksAsync(List<string> channels, string severity)
    {
        _logger.LogInformation($"Auto-assigning {channels.Count} outage tasks with severity {severity}...");
        await Task.CompletedTask;
    }

    private async Task HandleAdminCommandsAsync()
    {
        try
        {
            var updates = await _botClient.GetUpdatesAsync();
            foreach (var update in updates)
            {
                if (update.Message?.Text is not { } text) continue;

                if (text.StartsWith("/status"))
                {
                    var currentCount = _cache.TryGetValue(LastCountKey, out int cnt) ? cnt : 0;
                    await _telegramService.SentMessageToTelegram($"📡 Monitoring active. Current outages: {currentCount}");
                }
                else if (text.StartsWith("/report"))
                {
                    var report = BuildDailyReport();
                    await _telegramService.SentMessageToTelegram(report);
                }
                else if (text.StartsWith("/mute"))
                {
                    var parts = text.Split(' ');
                    if (parts.Length > 1 && int.TryParse(parts[1], out var mins))
                    {
                        _cache.Set(MuteAlertsKey, DateTime.UtcNow.AddMinutes(mins), TimeSpan.FromMinutes(mins + 1));
                        await _telegramService.SentMessageToTelegram($"🔇 Alerts muted for {mins} minutes.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling admin commands.");
        }
    }
}
