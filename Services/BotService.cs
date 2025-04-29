using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Services.Interfaces;

namespace Services
{
    public class BotService : IBotService
    {
        private ITelegramBotClient? _botClient;
        private readonly ICommandHandler _commandHandler;
        private readonly ICallbackHandler _callbackHandler;
        private readonly CancellationTokenSource _cts;
        private readonly string _apiToken;

        public BotService(string apiToken, ICommandHandler commandHandler, ICallbackHandler callbackHandler)
        {
            _apiToken = apiToken;
            _commandHandler = commandHandler;
            _callbackHandler = callbackHandler;
            _cts = new CancellationTokenSource();
        }

        public async Task StartAsync()
        {
            _botClient = new TelegramBotClient(_apiToken);

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
            };

            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                _cts.Token);

            var me = await _botClient.GetMe(_cts.Token);
            Console.WriteLine($"@{me.Username} is running... Press CTRL+C to terminate");
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        public ITelegramBotClient GetClient()
        {
            if (_botClient == null)
            {
                throw new InvalidOperationException("Bot client has not been initialized. Call StartAsync first.");
            }
            return _botClient;
        }

        public CancellationToken GetCancellationToken() => _cts.Token;

        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
        {
            try
            {
                if (update.Message is { } message)
                {
                    if (message.Text is { } text)
                        await _commandHandler.HandleTextMessageAsync(bot, message, text, token);
                }
                else if (update.CallbackQuery is { } callback)
                {
                    await _callbackHandler.HandleCallbackQueryAsync(bot, callback, token);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling update: {ex}");
            }
        }

        private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken token)
        {
            Console.WriteLine($"Error polling Telegram API: {ex.Message}");
            return Task.CompletedTask;
        }
    }
}
