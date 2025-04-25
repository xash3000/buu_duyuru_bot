using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Globalization;
using System.Text;
using Services;

// TODO: refactor this file into multiple classes

string apiToken = Environment.GetEnvironmentVariable("BUU_DUYURU_BOT_API_TOKEN") ?? string.Empty;

var scraper = new ScraperService();
var dbService = new DatabaseService();

dbService.InitializeDatabase();
var departments = await dbService.GetDepartmentsAsync();

using var cts = new CancellationTokenSource();
var botClient = new TelegramBotClient(apiToken);

// track pending actions ("follow" or "unfollow") per chat
var pendingActions = new Dictionary<long, string>();

var receiverOptions = new ReceiverOptions
{
  AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
};

botClient.StartReceiving(
    HandleUpdateAsync,
    HandleErrorAsync,
    receiverOptions,
    cts.Token);

var me = await botClient.GetMe(cts.Token);
Console.WriteLine($"@{me.Username} is running... Press CTRL+C to terminate");


// start periodic fetch of announcements
_ = PeriodicFetchAndSendAsync(botClient, cts.Token);

await Task.Delay(Timeout.Infinite, cts.Token);
cts.Cancel();

/// <summary>
/// Handles incoming updates from the Telegram Bot API asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="update">The update received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
{
  if (update.Message is { } message)
  {
    if (message.Text is { } text)
      await HandleTextMessageAsync(bot, message, text, token);
  }
  else if (update.CallbackQuery is { } callback)
  {
    await HandleCallbackQueryAsync(bot, callback, token);
  }
}

/// <summary>
/// Handles text messages received from Telegram users asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="messageText">The text content of the message</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleTextMessageAsync(ITelegramBotClient bot, Message message, string messageText, CancellationToken token)
{
  var parts = messageText.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
  var cmd = parts[0].Split('@')[0].ToLower();
  if (pendingActions.TryGetValue(message.Chat.Id, out var pending) && !messageText.StartsWith("/"))
  {
    await HandlePendingActionAsync(bot, message, messageText, token);
    return;
  }
  var arg = parts.Length > 1 ? parts[1] : string.Empty;

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
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleStartAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  await bot.SendMessage(message.Chat.Id, "Hoşgeldiniz! Komutları görmek için /help yazın.", cancellationToken: token);
}

/// <summary>
/// Handles the /help command asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleHelpAsync(ITelegramBotClient bot, Message message, CancellationToken token)
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
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleSubscribeAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  await bot.SendMessage(message.Chat.Id,
      "Lütfen takip etmek istediğiniz bölümü yazın (veya 'iptal' yazarak iptal edin):",
      cancellationToken: token);
  pendingActions[message.Chat.Id] = "follow";
}

/// <summary>
/// Handles the /unfollow command asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleUnsubscribeAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  var subsShortNames = await dbService.GetUserSubscriptionsAsync(message.Chat.Id);
  if (!subsShortNames.Any())
  {
    await bot.SendMessage(message.Chat.Id,
        "Takip ettiğiniz bölüm bulunmuyor.",
        cancellationToken: token);
  }
  else
  {
    var subsDepts = departments.Where(d => subsShortNames.Contains(d.ShortName)).ToList();
    var rows = new List<InlineKeyboardButton[]>();
    foreach (var dept in subsDepts)
    {
      rows.Add(new[] {
        InlineKeyboardButton.WithCallbackData(
          text: dept.Name,
          callbackData: $"unfollow:{dept.InsId}")
      });
    }
    rows.Add(new[] {
      InlineKeyboardButton.WithCallbackData(
        text: "iptal",
        callbackData: "cancel")
    });

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
async Task HandlePendingActionAsync(ITelegramBotClient bot, Message message, string query, CancellationToken token)
{
  if (NormalizeText(query) == "iptal")
  {
    pendingActions.Remove(message.Chat.Id);
    await bot.SendMessage(message.Chat.Id,
        "İşlem iptal edildi.", cancellationToken: token);
    return;
  }
  var action = pendingActions[message.Chat.Id];
  string norm = NormalizeText(query);
  var list = action == "follow" ? departments : (await dbService.GetUserSubscriptionsAsync(message.Chat.Id)
                .ContinueWith(t => departments.Where(d => t.Result.Contains(d.ShortName)).ToList()));
  var matches = list.Where(d => NormalizeText(d.Name).Contains(norm)
                          || NormalizeText(d.ShortName).Contains(norm)).ToList();
  if (!matches.Any())
  {
    await bot.SendMessage(message.Chat.Id,
        "Bölüm bulunamadı. Lütfen tekrar deneyin:", cancellationToken: token);
    return;
  }

  var rows = new List<InlineKeyboardButton[]>();
  foreach (var dept in matches)
  {
    rows.Add(new[] {
      InlineKeyboardButton.WithCallbackData(
        text: dept.Name,
        callbackData: $"{action}:{dept.InsId}")
    });
  }
  rows.Add(new[] {
    InlineKeyboardButton.WithCallbackData(
      text: "iptal",
      callbackData: "cancel")
  });

  var markup = new InlineKeyboardMarkup(rows);
  await bot.SendMessage(message.Chat.Id,
      "Lütfen bir bölüm seçin:", replyMarkup: markup, cancellationToken: token);
  pendingActions.Remove(message.Chat.Id);
}

/// <summary>
/// Handles follow/unfollow button callbacks.
/// </summary>
async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery callback, CancellationToken token)
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
    await bot.EditMessageText(targetChatId, messageId,
        "İşlem iptal edildi.", cancellationToken: token);
    await bot.AnswerCallbackQuery(callback.Id, cancellationToken: token);
    return;
  }
  var parts = callback.Data.Split(':', 2);
  if (parts.Length != 2 || !int.TryParse(parts[1], out var insId))
  {
    await bot.AnswerCallbackQuery(callback.Id, cancellationToken: token);
    return;
  }
  var action = parts[0];
  string response;
  if (action == "follow")
  {
    var user = callback.From;
    response = await (dbService.AddSubscriptionAsync(targetChatId, insId, user.Username, GetUserFullName(user))
      .ContinueWith(t => t.Result ? "Takip edildi." : "Zaten takip ediyorsunuz."));
  }
  else
  {
    response = await dbService.RemoveSubscriptionAsync(targetChatId, insId)
      .ContinueWith(t => t.Result ? "Takipten çıkıldı." : "Zaten takip etmiyorsunuz.");
  }
  await bot.EditMessageText(targetChatId, messageId, response, cancellationToken: token);
  await bot.AnswerCallbackQuery(callback.Id, cancellationToken: token);
}

/// <summary>
/// Normalizes text by removing diacritics and converting to lowercase.
/// </summary>
string NormalizeText(string text)
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
/// Handles the /my command asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleMyAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  var subsShortNames = await dbService.GetUserSubscriptionsAsync(message.Chat.Id);
  if (!subsShortNames.Any())
  {
    await bot.SendMessage(message.Chat.Id, "Takip ettiğiniz bölüm bulunmuyor.", cancellationToken: token);
    return;
  }

  var subsDepts = departments.Where(d => subsShortNames.Contains(d.ShortName)).ToList();
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
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleUnknownAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  await bot.SendMessage(message.Chat.Id, "Bilinmeyen komut. /help kullanın.", cancellationToken: token);
}

/// <summary>
/// Handles errors from the Telegram Bot API asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="ex">The exception that occurred</param>
/// <param name="token">Cancellation token for the async operation</param>
Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken token)
{
  Console.WriteLine($"Error: {ex.Message}");
  return Task.CompletedTask;
}

/// <summary>
/// Periodically fetches announcements and sends them to subscribed users asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task PeriodicFetchAndSendAsync(ITelegramBotClient bot, CancellationToken token)
{
  await FetchAndSendAsync(bot, token);
  var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
  try
  {
    while (await timer.WaitForNextTickAsync(token))
    {
      await FetchAndSendAsync(bot, token);
    }
  }
  catch (OperationCanceledException) { }
}

/// <summary>
/// Fetches new announcements and sends them to subscribed users asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task FetchAndSendAsync(ITelegramBotClient bot, CancellationToken token)
{
  var announcements = await scraper.FetchAnnouncementsAsync(departments);

  foreach (var ann in announcements)
  {
    if (!await dbService.AnnouncementExistsAsync(ann.Link))
    {
      Console.WriteLine($"[Periodic] Sending announcement: {ann.Link}");
      await dbService.InsertAnnouncementAsync(ann.InsId, ann.Link, ann.Title, ann.AddedDate);

      // Find the department from the list based on InsId
      var department = departments.FirstOrDefault(d => d.InsId == ann.InsId);
      if (department == null) continue; // Skip if department not found

      var subs = await dbService.GetSubscribersAsync(ann.InsId);
      foreach (var chatId in subs)
      {
        await bot.SendMessage(
            chatId: chatId,
            text:
                "📢 Duyuru\n\n" +
                $"👤 Kimden: {department.Name}  \n" +
                $"📅 Tarih: {ann.AddedDate:dd.MM.yyyy}  \n\n" +
                "🗒 Konu:\n" +
                $"> {ann.Title}\n\n" +
                $"🔗 {ann.Link}",
            linkPreviewOptions: new LinkPreviewOptions
            {
              IsDisabled = true
            },
            parseMode: ParseMode.Markdown,
            cancellationToken: token
        );
      }
    }
  }
}

/// <summary>
/// Gets the full name of a Telegram user.
/// </summary>
/// <param name="user">The Telegram user</param>
/// <returns>The full name of the user, or a fallback if unavailable</returns>
string GetUserFullName(User? user)
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
