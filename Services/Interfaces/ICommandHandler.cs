using Telegram.Bot;
using Telegram.Bot.Types;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Interfaces
{
    public interface ICommandHandler
    {
        Task HandleTextMessageAsync(ITelegramBotClient bot, Message message, string messageText, CancellationToken token);
    }
}
