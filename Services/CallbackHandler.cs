using Telegram.Bot;
using Telegram.Bot.Types;
using Services.Interfaces;

namespace Services
{
    public class CallbackHandler : ICallbackHandler
    {
        private readonly IDatabaseService _dbService;

        public CallbackHandler(IDatabaseService dbService)
        {
            _dbService = dbService;
        }

        /// <summary>
        /// Handles follow/unfollow button callbacks.
        /// </summary>
        public async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery callback, CancellationToken token)
        {
            if (callback.Data == null || callback.Message == null)
            {
                await bot.AnswerCallbackQuery(callback.Id, cancellationToken: token);
                return;
            }

            var targetChatId = callback.Message.Chat.Id;
            var messageId = callback.Message.MessageId;

            if (callback.Data == "cancel")
            {
                try
                {
                    await bot.EditMessageText(targetChatId, messageId,
                        "İşlem iptal edildi.", cancellationToken: token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error editing message on cancel: {ex.Message}");
                }
                finally
                {
                    await bot.AnswerCallbackQuery(callback.Id, cancellationToken: token);
                }
                return;
            }

            // Parse action and department ID
            var parts = callback.Data.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var insId))
            {
                Console.WriteLine($"Invalid callback data format: {callback.Data}");
                await bot.AnswerCallbackQuery(callback.Id, "Geçersiz işlem.", cancellationToken: token);
                return;
            }

            var action = parts[0];
            string responseText;
            bool success = false;

            try
            {
                if (action == "follow")
                {
                    var user = callback.From;
                    success = await _dbService.AddSubscriptionAsync(targetChatId, insId, user.Username, CommandHandler.GetUserFullName(user));
                    responseText = success ? "Takip edildi." : "Zaten takip ediyorsunuz.";
                }
                else if (action == "unfollow")
                {
                    success = await _dbService.RemoveSubscriptionAsync(targetChatId, insId);
                    responseText = success ? "Takipten çıkıldı." : "Zaten takip etmiyorsunuz.";
                }
                else
                {
                    responseText = "Bilinmeyen işlem.";
                    Console.WriteLine($"Unknown callback action: {action}");
                }

                await bot.EditMessageText(targetChatId, messageId, responseText, cancellationToken: token);
                await bot.AnswerCallbackQuery(callback.Id, cancellationToken: token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing callback {action}:{insId} for chat {targetChatId}: {ex}");
                await bot.AnswerCallbackQuery(callback.Id, "Bir hata oluştu.", cancellationToken: token);
                try
                {
                    await bot.EditMessageText(targetChatId, messageId, "İşlem sırasında bir hata oluştu.", cancellationToken: token);
                }
                catch { /* Ignore secondary error */ }
            }
        }
    }
}
