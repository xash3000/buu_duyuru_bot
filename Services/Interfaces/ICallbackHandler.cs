using Telegram.Bot;
using Telegram.Bot.Types;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Interfaces
{
    public interface ICallbackHandler
    {
        Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery callback, CancellationToken token);
    }
}
