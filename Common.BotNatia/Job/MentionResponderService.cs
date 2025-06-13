using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Common.BotNatia.Job;

public class MentionResponderService : BackgroundService
{
    private readonly TelegramBotClient _botClient;
    private readonly ILogger<MentionResponderService> _logger;
    private readonly string _botUsername = "@NatiaAlert_bot";

    public MentionResponderService(ILogger<MentionResponderService> logger)
    {
        _logger = logger;
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

        if (text.Contains("სტატუსი", StringComparison.OrdinalIgnoreCase) || text.Contains("status", StringComparison.OrdinalIgnoreCase))
            response = "🟢 არხების მონიტორინგი აქტიურია. სერიოზული გათიშვები ამ დროისთვის არ ფიქსირდება.";
        else if (text.Contains("გამარჯობა", StringComparison.OrdinalIgnoreCase) || text.Contains("hello", StringComparison.OrdinalIgnoreCase))
            response = "👋 გამარჯობა, მე ვარ ნათია. ვაკვირდები არხების მდგომარეობას 24/7.";
        else
            response = "🤖 გთხოვთ გამოიყენოთ `სტატუსი`, `გამარჯობა` ან `status`.";

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
}
