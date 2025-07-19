using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Common.BotNatia.MesageSender;

public class BootSendInfo
{
    public async Task SentMessageToTelegram(string Message)
    {
        var botToken = "7992931942:AAHAfog7gNKm1yaAoNe4FZeEhdjmet2Zi7U";
        var chatId = -1002817849163;

        var botClient = new TelegramBotClient(botToken);

        var message = Message;

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: message,
            parseMode: ParseMode.Markdown,
            cancellationToken: CancellationToken.None
        );
    }
}
