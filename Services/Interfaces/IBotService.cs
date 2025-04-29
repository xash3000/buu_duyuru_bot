using Telegram.Bot;

namespace Services.Interfaces
{
    public interface IBotService
    {
        Task StartAsync();
        void Stop();
        ITelegramBotClient GetClient();
        CancellationToken GetCancellationToken();
    }
}
