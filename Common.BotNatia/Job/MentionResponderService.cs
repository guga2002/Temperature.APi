using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Common.BotNatia.Interfaces;
using Common.BotNatia.Models;
using System.Data;
using Dapper;
using Newtonsoft.Json;

namespace Common.BotNatia.Job;

public class MentionResponderService : BackgroundService
{
    private readonly TelegramBotClient _botClient;
    private readonly ILogger<MentionResponderService> _logger;
    private readonly string _botUsername = "@NatiaAlert_bot";
    private readonly IServiceProvider _serviceProvider;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private (DateTime Timestamp, (int Total, int Errors, int Criticals) Data)? _cachedAnalytics;
    private (DateTime Timestamp, NatiaLog? Data)? _cachedCritical;
    private (DateTime Timestamp, TemperatureResponse? Data)? _cachedTemperature;

    public MentionResponderService(ILogger<MentionResponderService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _botClient = new TelegramBotClient("7992931942:AAHAfog7gNKm1yaAoNe4FZeEhdjmet2Zi7U");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: stoppingToken
        );

        _logger.LogInformation("📡 MentionResponderService started.");
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message || message.Text is not { } text || message.Entities is null)
            return;

        var isMentioned = message.Entities.Any(e =>
            e.Type == MessageEntityType.Mention &&
            text.Substring(e.Offset, e.Length).Equals(_botUsername, StringComparison.OrdinalIgnoreCase));

        if (!isMentioned) return;

        try
        {
            string response = await ProcessCommandAsync(text);
            await bot.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: response,
                parseMode: ParseMode.Markdown,
                replyToMessageId: message.MessageId,
                cancellationToken: ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to process command.");
            await bot.SendTextMessageAsync(message.Chat.Id, "⚠️ შეცდომა ბრძანების შესრულებისას.", cancellationToken: ct);
        }
    }

    private async Task<string> ProcessCommandAsync(string text)
    {
        if (text.Contains("გამარჯობა", StringComparison.OrdinalIgnoreCase) || text.Contains("hello", StringComparison.OrdinalIgnoreCase))
            return "👋 გამარჯობა, მე ვარ ნათია. ვაკვირდები არხებს 24/7.";

        if (text.Contains("სტატუსი", StringComparison.OrdinalIgnoreCase))
            return await GetStatusAsync();

        if (text.Contains("/uptime"))
        {
            var uptime = DateTime.UtcNow - _startedAt;
            return $"⏱️ ბოტი მუშაობს {uptime.TotalHours:F1} საათია (`{uptime:hh\\:mm\\:ss}`)";
        }

        if (text.Contains("/ბოლოგათიშვა"))
            return await GetLastCriticalAsync();

        if (text.Contains("ტემპერატურა", StringComparison.OrdinalIgnoreCase) || text.Contains("humidity", StringComparison.OrdinalIgnoreCase))
            return await GetTemperatureAsync();

        if (text.Contains("/ანალიტიკა"))
            return await GetAnalyticsAsync();

        if (text.Contains("/შეფასება") || text.Contains("/feedback"))
        {
            var feedback = await GetNatiaFeedbackAsync();
            return feedback != null
                ? $"📣 *ნათიას შეფასება:*\n_{feedback}_"
                : "⚠️ შეფასება ამ დროისთვის მიუწვდომელია.";
        }

        if (text.Contains("/help") || text.Contains("დახმარება", StringComparison.OrdinalIgnoreCase))
            return GetHelpText();

        return "🤖 გამოიყენეთ `help` ან `დახმარება` ბრძანებები.";
    }

    private async Task<string> GetStatusAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var chanelService = scope.ServiceProvider.GetRequiredService<IChanellServices>();

        var ports = await chanelService.GetPortsWhereAlarmsIsOn();
        if (!ports.Any())
            return "✅ ამ ეტაპზე ყველა არხი კარგად მუშაობს.";

        var channels = await chanelService.GetChannelsByPortIn250ListAsync(ports);
        return $"🚨 გათიშულია {ports.Count} არხი:\n" + string.Join("\n", channels);
    }

    private async Task<string> GetLastCriticalAsync()
    {
        if (_cachedCritical is { } cache && (DateTime.UtcNow - cache.Timestamp) < TimeSpan.FromSeconds(30))
        {
            return FormatCritical(cache.Data);
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        const string sql = @"SELECT TOP 1 * FROM neurals WHERE IsCritical = 1 ORDER BY ActionDate DESC";
        var last = await db.QueryFirstOrDefaultAsync<NatiaLog>(sql);
        _cachedCritical = (DateTime.UtcNow, last);
        return FormatCritical(last);
    }

    private string FormatCritical(NatiaLog? log)
    {
        return log != null
            ? $"📡 ბოლო სერიოზული პრობლემა დაფიქსირდა {log.ActionDate:g}\n" +
              $"*არხი:* `{log.ChannelName}`\n" +
              $"_შეცდომა:_ `{log.ErrorMessage}`\n" +
              $"🔧 რეკომენდაცია: {log.SuggestedSolution}"
            : "✅ ბოლო 24 საათში კრიტიკული შეცდომა არ დაფიქსირებულა.";
    }

    private async Task<string> GetAnalyticsAsync()
    {
        if (_cachedAnalytics is { } cache && (DateTime.UtcNow - cache.Timestamp) < TimeSpan.FromSeconds(30))
        {
            var data = cache.Data;
            return $"🧾 ბოლო 24 საათში:\n- ლოგები: {data.Total}\n- შეცდომები: {data.Errors}\n- კრიტიკულები: {data.Criticals}";
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        const string sql = @"
            SELECT
                COUNT(*) AS Total,
                SUM(CASE WHEN IsError = 1 THEN 1 ELSE 0 END) AS Errors,
                SUM(CASE WHEN IsCritical = 1 THEN 1 ELSE 0 END) AS Criticals
            FROM neurals
            WHERE ActionDate >= DATEADD(DAY, -1, GETDATE())";
        var result = await db.QueryFirstOrDefaultAsync<(int, int, int)>(sql);
        _cachedAnalytics = (DateTime.UtcNow, result);

        return $"🧾 ბოლო 24 საათში:\n- ლოგები: {result.Item1}\n- შეცდომები: {result.Item2}\n- კრიტიკულები: {result.Item3}";
    }

    private async Task<string> GetTemperatureAsync()
    {
        if (_cachedTemperature is { } cache && (DateTime.UtcNow - cache.Timestamp) < TimeSpan.FromSeconds(30))
            return FormatTemperature(cache.Data);

        try
        {
            var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var httpClient = new HttpClient(handler);
            var response = await httpClient.GetAsync("https://192.168.0.79:2000/api/Temprature/GetCurrentTemperature");

            if (!response.IsSuccessStatusCode)
                return "⚠️ ტემპერატურის მიღება ვერ მოხერხდა.";

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<TemperatureResponse>(json);
            _cachedTemperature = (DateTime.UtcNow, data);
            return FormatTemperature(data);
        }
        catch
        {
            return "⚠️ ტემპერატურის მიღება ვერ მოხერხდა.";
        }
    }

    private string FormatTemperature(TemperatureResponse? data)
    {
        return data == null
            ? "⚠️ ტემპერატურის მონაცემები მიუწვდომელია."
            : $"🌡️ ტემპერატურა: {data.Temperature} °C\n💧 ტენიანობა: {data.Humidity} %";
    }

    private async Task<string?> GetNatiaFeedbackAsync()
    {
        try
        {
            using var httpClient = new HttpClient();
            return await httpClient.GetStringAsync("http://192.168.1.102:3395/api/NatiaCore/natiaFeedback");
        }
        catch
        {
            return null;
        }
    }

    private string GetHelpText()
    {
        return @"🛠 *ნათიას მხარდაჭერილი ბრძანებები*:
🟢 `@NatiaAlert_bot სტატუსი` – არხების სტატუსი  
📊 `/ანალიტიკა` – ბოლო 24 საათის ლოგების ანალიზი  
📡 `/ბოლოგათიშვა` – ბოლო კრიტიკული შეცდომა  
🌡️ `ტემპერატურა` ან `humidity` – სადგურის მონაცემები  
👋 `გამარჯობა` – მისალმება  
⏱ `/uptime` – ბოტის მუშაობის დრო  
💬 `/შეფასება` ან `/feedback` – ნათიას შეფასება  
❓ `/help` ან `დახმარება` – ყველა ბრძანების ნახვა";
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "❌ Error in MentionResponderService");
        return Task.CompletedTask;
    }
}
