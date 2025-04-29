using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using System.Globalization;
using System.Text;
using Models;
using Services.Interfaces;

namespace Services
{
    public class CommandHandler : ICommandHandler
    {
        private readonly IDatabaseService _dbService;
        private readonly List<Department> _departments;
        private readonly Dictionary<long, string> _pendingActions = new();

        public CommandHandler(IDatabaseService dbService, List<Department> departments)
        {
            _dbService = dbService;
            _departments = departments;
        }

        /// <summary>
        /// Handles text messages received from Telegram users asynchronously.
        /// </summary>
        public async Task HandleTextMessageAsync(ITelegramBotClient bot, Message message, string messageText, CancellationToken token)
        {
            var parts = messageText.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
            var cmd = parts[0].Split('@')[0].ToLower();

            if (_pendingActions.TryGetValue(message.Chat.Id, out var pending) && !messageText.StartsWith("/"))
            {
                await HandlePendingActionAsync(bot, message, messageText, token);
                return;
            }

            switch (cmd)
            {
                case "/start": await HandleStartAsync(bot, message, token); break;
                case "/help": await HandleHelpAsync(bot, message, token); break;
                case "/follow": await HandleSubscribeAsync(bot, message, token); break;
                case "/unfollow": await HandleUnsubscribeAsync(bot, message, token); break;
                case "/my": await HandleMyAsync(bot, message, token); break;
                default: await HandleUnknownAsync(bot, message, token); break;
            }
        }

        /// <summary>
        /// Handles the /start command asynchronously.
        /// </summary>
        private async Task HandleStartAsync(ITelegramBotClient bot, Message message, CancellationToken token)
        {
            await bot.SendMessage(message.Chat.Id, "Hoşgeldiniz! Komutları görmek için /help yazın.", cancellationToken: token);
        }

        /// <summary>
        /// Handles the /help command asynchronously.
        /// </summary>
        private async Task HandleHelpAsync(ITelegramBotClient bot, Message message, CancellationToken token)
        {
            await bot.SendMessage(message.Chat.Id,
                "/follow - bölümleri takip et\n" +
                "/unfollow - takipten çık\n" +
                "/my - takiplerini göster\n",
                cancellationToken: token);
        }

        /// <summary>
        /// Handles the /follow command asynchronously.
        /// </summary>
        private async Task HandleSubscribeAsync(ITelegramBotClient bot, Message message, CancellationToken token)
        {
            await bot.SendMessage(message.Chat.Id,
                "Lütfen takip etmek istediğiniz bölümü yazın (veya 'iptal' yazarak iptal edin):",
                cancellationToken: token);
            _pendingActions[message.Chat.Id] = "follow";
        }

        /// <summary>
        /// Handles the /unfollow command asynchronously.
        /// </summary>
        private async Task HandleUnsubscribeAsync(ITelegramBotClient bot, Message message, CancellationToken token)
        {
            var subsShortNames = await _dbService.GetUserSubscriptionsAsync(message.Chat.Id);
            if (!subsShortNames.Any())
            {
                await bot.SendMessage(message.Chat.Id,
                    "Takip ettiğiniz bölüm bulunmuyor.",
                    cancellationToken: token);
            }
            else
            {
                var subsDepts = _departments.Where(d => subsShortNames.Contains(d.ShortName)).ToList();
                var rows = new List<InlineKeyboardButton[]>();
                foreach (var dept in subsDepts)
                {
                    rows.Add([
                        InlineKeyboardButton.WithCallbackData(
                            text: dept.Name,
                            callbackData: $"unfollow:{dept.InsId}")
                    ]);
                }
                rows.Add([
                    InlineKeyboardButton.WithCallbackData(
                        text: "iptal",
                        callbackData: "cancel")
                ]);

                var markup = new InlineKeyboardMarkup(rows);
                await bot.SendMessage(message.Chat.Id,
                    "Lütfen takipten çıkmak istediğiniz bölümü seçin:",
                    replyMarkup: markup,
                    cancellationToken: token);
            }
        }

        /// <summary>
        /// Processes pending follow/unfollow searches.
        /// </summary>
        private async Task HandlePendingActionAsync(ITelegramBotClient bot, Message message, string query, CancellationToken token)
        {
            if (NormalizeText(query) == "iptal")
            {
                _pendingActions.Remove(message.Chat.Id);
                await bot.SendMessage(message.Chat.Id,
                    "İşlem iptal edildi.", cancellationToken: token);
                return;
            }

            if (!_pendingActions.TryGetValue(message.Chat.Id, out var action))
            {
                await bot.SendMessage(message.Chat.Id, "Bekleyen bir işlem bulunamadı.", cancellationToken: token);
                return;
            }

            string norm = NormalizeText(query);
            List<Department> list;

            if (action == "follow")
            {
                list = _departments;
            }
            else // action == "unfollow" 
            {
                var userSubs = await _dbService.GetUserSubscriptionsAsync(message.Chat.Id);
                list = _departments.Where(d => userSubs.Contains(d.ShortName)).ToList();
            }


            var matches = list.Where(d => NormalizeText(d.Name).Contains(norm)
                                    || NormalizeText(d.ShortName).Contains(norm)).ToList();
            if (!matches.Any())
            {
                await bot.SendMessage(message.Chat.Id,
                    "Bölüm bulunamadı. Lütfen tekrar deneyin (veya 'iptal' yazın):", cancellationToken: token);
                return;
            }

            var rows = new List<InlineKeyboardButton[]>();
            foreach (var dept in matches)
            {
                rows.Add([
                    InlineKeyboardButton.WithCallbackData(
                        text: dept.Name,
                        callbackData: $"{action}:{dept.InsId}")
                ]);
            }
            rows.Add([
                InlineKeyboardButton.WithCallbackData(
                    text: "iptal",
                    callbackData: "cancel")
            ]);

            var markup = new InlineKeyboardMarkup(rows);
            await bot.SendMessage(message.Chat.Id,
                "Lütfen bir bölüm seçin:", replyMarkup: markup, cancellationToken: token);

            _pendingActions.Remove(message.Chat.Id);
        }


        /// <summary>
        /// Handles the /my command asynchronously.
        /// </summary>
        private async Task HandleMyAsync(ITelegramBotClient bot, Message message, CancellationToken token)
        {
            var subsShortNames = await _dbService.GetUserSubscriptionsAsync(message.Chat.Id);
            if (!subsShortNames.Any())
            {
                await bot.SendMessage(message.Chat.Id, "Takip ettiğiniz bölüm bulunmuyor.", cancellationToken: token);
                return;
            }

            var subsDepts = _departments.Where(d => subsShortNames.Contains(d.ShortName)).ToList();
            var deptNames = subsDepts.Select(d => d.Name).ToList();

            await bot.SendMessage(
                message.Chat.Id,
                $"Takip ettiğiniz bölümler:\n\n{string.Join("\n", deptNames)}",
                cancellationToken: token
            );
        }

        /// <summary>
        /// Handles unknown commands asynchronously.
        /// </summary>
        private async Task HandleUnknownAsync(ITelegramBotClient bot, Message message, CancellationToken token)
        {
            await bot.SendMessage(message.Chat.Id, "Bilinmeyen komut. /help kullanın.", cancellationToken: token);
        }

        /// <summary>
        /// Normalizes text by removing diacritics and converting to lowercase.
        /// </summary>
        private string NormalizeText(string text)
        {
            var formD = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in formD)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        /// <summary>
        /// Gets the full name of a Telegram user.
        /// </summary>
        public static string GetUserFullName(User? user)
        {
            if (user == null) return "Unknown User";

            // Combine first and last name if both available
            if (!string.IsNullOrEmpty(user.FirstName) && !string.IsNullOrEmpty(user.LastName))
                return $"{user.FirstName} {user.LastName}";

            // First name only
            if (!string.IsNullOrEmpty(user.FirstName))
                return user.FirstName;

            // Username only if that's all we have
            if (!string.IsNullOrEmpty(user.Username))
                return $"@{user.Username}";

            return "Unknown User";
        }
    }
}