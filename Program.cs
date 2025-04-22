using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Services;
using Models;
using System.Diagnostics.Eventing.Reader;
#pragma warning disable CS0618 // Suppress obsolete SendTextMessageAsync warnings

var dbService = new DatabaseService();
dbService.InitializeDatabase();

// seed initial departments
var initialDepartments = new List<Department>
{
    new Department { Name = "Türk Dili", ShortName = "turkdili", Url = "https://uludag.edu.tr/turkdili/duyuru", InsId = 573 }
};
foreach (var d in initialDepartments)
  await dbService.AddDepartmentAsync(d);
// load departments from database
var departments = await dbService.GetDepartmentsAsync();

string apiToken = Environment.GetEnvironmentVariable("BUU_DUYURU_BOT_API_TOKEN") ?? string.Empty;

var scraper = new ScraperService();

using var cts = new CancellationTokenSource();
var botClient = new TelegramBotClient(apiToken);

var me = await botClient.GetMe(cts.Token);
Console.WriteLine($"@{me.Username} is running... Press Enter to terminate");

var receiverOptions = new ReceiverOptions
{
  AllowedUpdates = new[] { UpdateType.Message }
};

botClient.StartReceiving(
    HandleUpdateAsync,
    HandleErrorAsync,
    receiverOptions,
    cts.Token);
// start periodic fetch
_ = PeriodicFetchAndSendAsync(botClient, cts.Token);
#if DEBUG
await Task.Delay(Timeout.Infinite, cts.Token);
#else
Console.ReadLine();
cts.Cancel();
#endif

/// <summary>
/// Handles incoming updates from the Telegram Bot API asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="update">The update received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
{
  Console.WriteLine($"[Debug] Received update of type: {update.Type}");
  if (update.Message is { } message)
  {
    Console.WriteLine($"[Debug] Received message.Text: {message.Text}");
    if (message.Text is { } text)
      await HandleTextMessageAsync(bot, message, text, token);
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
  Console.WriteLine($"[Debug] Handling command: {messageText} from chat {message.Chat.Id}");
  var parts = messageText.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
  var cmd = parts[0].Split('@')[0].ToLower();
  var arg = parts.Length > 1 ? parts[1].ToLower() : string.Empty;

  switch (cmd)
  {
    case "/start": await HandleStartAsync(bot, message, token); break;
    case "/help": await HandleHelpAsync(bot, message, token); break;
    case "/list": await HandleListAsync(bot, message, token); break;
    case "/subscribe": await HandleSubscribeAsync(bot, message, token); break;
    case "/unsubscribe": await HandleUnsubscribeAsync(bot, message, arg, token); break;
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
  await bot.SendMessage(message.Chat.Id, "Welcome! Use /help to see commands.", cancellationToken: token);
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
      "/list - available departments\n" +
      "/subscribe <dept> - subscribe with your Telegram username\n" +
      "/unsubscribe <dept> - unsubscribe\n" +
      "/my - show your subscriptions\n",
      cancellationToken: token);
}

/// <summary>
/// Handles the /list command asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleListAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  await bot.SendMessage(message.Chat.Id, string.Join(", ", departments.Select(d => d.ShortName)), cancellationToken: token);
}

/// <summary>
/// Handles the /subscribe command asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleSubscribeAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  var parts = message.Text?.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
  if (parts?.Length == 2)
  {
    var dept = parts[1].ToLower();
    var username = message.From?.Username;
    var fullName = GetUserFullName(message.From);

    if (departments.Any(d => d.ShortName == dept))
    {
      // Get department ID
      var department = departments.First(d => d.ShortName == dept);
      var added = await dbService.AddSubscriptionAsync(
          message.Chat.Id,
          department.InsId,
          username,
          fullName);

      await bot.SendMessage(message.Chat.Id, added ? $"Subscribed to {dept}." : "Already subscribed.", cancellationToken: token);
    }
    else await bot.SendMessage(message.Chat.Id, "Unknown department.", cancellationToken: token);
  }
  else await bot.SendMessage(message.Chat.Id, "Usage: /subscribe <dept>", cancellationToken: token);
}

/// <summary>
/// Handles the /unsubscribe command asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="arg">The department argument to unsubscribe from</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleUnsubscribeAsync(ITelegramBotClient bot, Message message, string arg, CancellationToken token)
{
  if (departments.Any(d => d.ShortName == arg))
  {
    var removed = await dbService.RemoveSubscriptionAsync(message.Chat.Id, int.Parse(arg));
    await bot.SendMessage(message.Chat.Id, removed ? $"Unsubscribed from {arg}." : "You were not subscribed.", cancellationToken: token);
  }
  else await bot.SendMessage(message.Chat.Id, "Unknown department.", cancellationToken: token);
}

/// <summary>
/// Handles the /my command asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleMyAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  var subs = await dbService.GetUserSubscriptionsAsync(message.Chat.Id);
  await bot.SendMessage(message.Chat.Id, subs.Any() ? string.Join(", ", subs) : "No subscriptions.", cancellationToken: token);
}

/// <summary>
/// Handles unknown commands asynchronously.
/// </summary>
/// <param name="bot">The Telegram Bot client instance</param>
/// <param name="message">The message received from Telegram</param>
/// <param name="token">Cancellation token for the async operation</param>
async Task HandleUnknownAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  await bot.SendMessage(message.Chat.Id, "Unknown command. Use /help.", cancellationToken: token);
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
  var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
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
                "🗒 Konu:  \n" +
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
