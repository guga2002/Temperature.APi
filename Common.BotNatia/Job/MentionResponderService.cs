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

    public MentionResponderService(ILogger<MentionResponderService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _botClient = new TelegramBotClient("7992931942:AAHAfog7gNKm1yaAoNe4FZeEhdjmet2Zi7U");
        _serviceProvider = serviceProvider;
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
        var scope = _serviceProvider.CreateScope();
        var chanelService = scope.ServiceProvider.GetRequiredService<IChanellServices>();
        if (update.Message is not { } message || message.Text is not { } text)
            return;

        if (message.Entities is null)
            return;

        var isMentioned = message.Entities.Any(e =>
            e.Type == MessageEntityType.Mention &&
            text.Substring(e.Offset, e.Length).Equals(_botUsername, StringComparison.OrdinalIgnoreCase));

        if (!isMentioned)
            return;

        string response;

        if (text.Contains("გამარჯობა", StringComparison.OrdinalIgnoreCase) || text.Contains("hello", StringComparison.OrdinalIgnoreCase))
            response = "👋 გამარჯობა, მე ვარ ნათია. ვაკვირდები არხების მდგომარეობას 24/7.";
        else if (text.Contains("სტატუსი", StringComparison.OrdinalIgnoreCase))
        {
            var ports = await chanelService.GetPortsWhereAlarmsIsOn();
            if (ports.Any())
            {
                response = "ამ ეტაპზე ყველა არხი კარგად არის";
            }
            else
            {
                var chanells = await chanelService.GetChannelsByPortIn250ListAsync(ports);
                response = $"გაგვეთიშა შემდეგ არხები, ჯამში:{ports.Count} არხი.";
                response += string.Join("\n", chanells);
            }
        }
        else if (text.Contains("/uptime"))
        {
            var uptime = DateTime.UtcNow - _startedAt;
            response = $"⏱️ ბოტი მუშაობს {uptime.TotalHours:F1} საათია ({uptime:hh\\:mm\\:ss})";
        }
        if (text.Contains("/ბოლოგათიშვა"))
        {
            var last = await GetLastCriticalAsync();
            if (last != null)
            {
                response = $"📡 ბოლო სერიოზული პრობლემა დაფიქსირდა {last.ActionDate:g} არხზე *{last.ChannelName}*\n" +
                           $"შეცდომა: _{last.ErrorMessage}_\n" +
                           $"🔧 რეკომენდაცია: {last.SuggestedSolution}";
            }
            else
            {
                response = "✅ ბოლო 24 საათში კრიტიკული შეცდომა არ დაფიქსირებულა.";
            }
        }
        else if (text.Contains("ტემპერატურა", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("humidity", StringComparison.OrdinalIgnoreCase))
        {
            var data = await GetCurrentTemperatureAsync();

            if (data == null)
            {
                response = "⚠️ ტემპერატურის მიღება ვერ მოხერხდა.";
            }
            else
            {
                response = $"🌡️ ტემპერატურა სადგურში: {data.Temperature} °C\n" +
                           $"💧 ტენიანობა: {data.Humidity} %";
            }
        }
        else if (text.Contains("/ანალიტიკა"))
        {
            var (total, errors, criticals) = await GetAnalyticsAsync();
            response = $"🧾 ბოლო 24 საათში:\n" +
                       $"- ჯამში ლოგები: {total}\n" +
                       $"- შეცდომები: {errors}\n" +
                       $"- კრიტიკულები: {criticals}";
        }
        else if (text.Contains("/შეფასება") || text.Contains("/feedback"))
        {
            var feedback = await GetNatiaFeedbackAsync();
            response = feedback != null
                ? $"📣 *ნათიას შეფასება:*\n_{feedback}_"
                : "⚠️ შეფასება ამ დროისთვის მიუწვდომელია.";
        }
        else if (text.Contains("/help") || text.Contains("დახმარება", StringComparison.OrdinalIgnoreCase))
        {
            response = @"🛠 *ნათიას ბოტის მხარდაჭერილი ბრძანებები*:
               🟢 `@NatiaAlert_bot სტატუსი` – არხების აქტუალური მდგომარეობა  
                📊 `/ანალიტიკა` – ბოლო 24 საათის ლოგების ანალიზი  
                 📡 `/ბოლოგათიშვა` – ბოლო კრიტიკული შეცდომა  
               🌡️ `ტემპერატურა` ან `humidity` – ამინდის სადგურის მონაცემები  
              👋 `გამარჯობა` – მისალმება  
               ❓ `/help` ან `დახმარება` – ყველა ბრძანების ნახვა
                uptime -  ბოტის გაშვების თარიღი.
               შეფასება, feedback : ნათიას შეფასება
            _გთხოვთ მიუთითოთ ბოტი ტექსტში, რათა გაგცეთ პასუხი._";
        }
        else
        {
            response = "🤖 გთხოვთ გამოიყენოთ `help`, `დახმარება`.";
        }

        await bot.SendTextMessageAsync(
            chatId: message.Chat.Id,
            text: response,
            parseMode: ParseMode.Markdown,
            replyToMessageId: message.MessageId,
            cancellationToken: ct
        );
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        _logger.LogError(exception, "❌ Error in MentionResponderService");
        return Task.CompletedTask;
    }


    private async Task<NatiaLog?> GetLastCriticalAsync()
    {
        var scope = _serviceProvider.CreateScope();
        var _db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        const string sql = @"
            SELECT TOP 1 *
            FROM neurals
            WHERE IsCritical = 1
            ORDER BY ActionDate DESC";

        return await _db.QueryFirstOrDefaultAsync<NatiaLog>(sql);
    }

    private async Task<(int Total, int Errors, int Criticals)> GetAnalyticsAsync()
    {
        var scope = _serviceProvider.CreateScope();
        var _db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        const string sql = @"
            SELECT
              COUNT(*) AS Total,
              SUM(CASE WHEN IsError = 1 THEN 1 ELSE 0 END) AS Errors,
              SUM(CASE WHEN IsCritical = 1 THEN 1 ELSE 0 END) AS Criticals
            FROM neurals
            WHERE ActionDate >= DATEADD(DAY, -1, GETDATE());";

        var result = await _db.QueryFirstOrDefaultAsync<(int Total, int Errors, int Criticals)>(sql);
        return result;
    }

    private async Task<TemperatureResponse?> GetCurrentTemperatureAsync() 
    {
        try
        {
            var _httpClient =new HttpClient();
            var response = await _httpClient.GetAsync("https://192.168.0.79:2000/api/Temprature/GetCurrentTemperature");

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<TemperatureResponse>(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> GetNatiaFeedbackAsync()
    {
        try
        {
            using var httpClient = new HttpClient();
            var result = await httpClient.GetStringAsync("http://192.168.1.102:3395/api/NatiaCore/natiaFeedback");
            return result;
        }
        catch
        {
            return null;
        }
    }
}
