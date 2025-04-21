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
    case "/fetch": await HandleFetchAsync(bot, message, token); break;
    default: await HandleUnknownAsync(bot, message, token); break;
  }
}

// Command handlers
async Task HandleStartAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  await bot.SendMessage(message.Chat.Id, "Welcome! Use /help to see commands.", cancellationToken: token);
}

async Task HandleHelpAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  await bot.SendMessage(message.Chat.Id,
      "/list - available departments\n" +
      "/subscribe <dept> - subscribe with your Telegram username\n" +
      "/unsubscribe <dept> - unsubscribe\n" +
      "/my - show your subscriptions\n" +
      "/fetch - fetch announcements now",
      cancellationToken: token);
}

async Task HandleListAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  await bot.SendMessage(message.Chat.Id, string.Join(", ", departments.Select(d => d.ShortName)), cancellationToken: token);
}

// Subscribe using only department and Telegram username
async Task HandleSubscribeAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  var parts = message.Text.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
  if (parts.Length == 2)
  {
    var dept = parts[1].ToLower();
    var userName = message.From?.Username ?? message.From?.FirstName;
    if (departments.Any(d => d.ShortName == dept))
    {
      var added = await dbService.AddSubscriptionAsync(message.Chat.Id, dept, userName);
      await bot.SendMessage(message.Chat.Id, added ? $"Subscribed to {dept}." : "Already subscribed.", cancellationToken: token);
    }
    else await bot.SendMessage(message.Chat.Id, "Unknown department.", cancellationToken: token);
  }
  else await bot.SendMessage(message.Chat.Id, "Usage: /subscribe <dept>", cancellationToken: token);
}

async Task HandleUnsubscribeAsync(ITelegramBotClient bot, Message message, string arg, CancellationToken token)
{
  if (departments.Any(d => d.ShortName == arg))
  {
    var removed = await dbService.RemoveSubscriptionAsync(message.Chat.Id, arg);
    await bot.SendMessage(message.Chat.Id, removed ? $"Unsubscribed from {arg}." : "You were not subscribed.", cancellationToken: token);
  }
  else await bot.SendMessage(message.Chat.Id, "Unknown department.", cancellationToken: token);
}

async Task HandleMyAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  var subs = await dbService.GetUserSubscriptionsAsync(message.Chat.Id);
  await bot.SendMessage(message.Chat.Id, subs.Any() ? string.Join(", ", subs) : "No subscriptions.", cancellationToken: token);
}

// Handle /fetch: perform fetch and send confirmation
async Task HandleFetchAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  Console.WriteLine($"[Debug] Manual fetch requested by chat {message.Chat.Id}");
  var announcements = await scraper.FetchAnnouncementsAsync(departments);
  int sentCount = 0;
  foreach (var ann in announcements)
  {
    Console.WriteLine($"[Debug] Manual send announcement: {ann.Link}");
    await bot.SendMessage(message.Chat.Id,
        $"Duyuru\nKimden: {ann.Department}\nTarih: {ann.AddedDate:dd.MM.yyyy}\n\n{ann.Title}\n\n{ann.Link}",
        cancellationToken: token);
    sentCount++;
  }
  await bot.SendMessage(message.Chat.Id,
      sentCount > 0 ? $"Fetch completed, sent {sentCount} announcements." : "Fetch completed, no announcements found.",
      cancellationToken: token);
}

async Task HandleUnknownAsync(ITelegramBotClient bot, Message message, CancellationToken token)
{
  await bot.SendMessage(message.Chat.Id, "Unknown command. Use /help.", cancellationToken: token);
}

Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken token)
{
  Console.WriteLine($"Error: {ex.Message}");
  return Task.CompletedTask;
}

async Task PeriodicFetchAndSendAsync(ITelegramBotClient bot, CancellationToken token)
{
  var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
  try
  {
    while (await timer.WaitForNextTickAsync(token))
    {
      await FetchAndSendAsync(bot, token);
    }
  }
  catch (OperationCanceledException) { }
}

async Task FetchAndSendAsync(ITelegramBotClient bot, CancellationToken token)
{
  var announcements = await scraper.FetchAnnouncementsAsync(departments);

  foreach (var ann in announcements)
  {
    if (!await dbService.AnnouncementExistsAsync(ann.Link))
    {
      Console.WriteLine($"[Periodic] Sending announcement: {ann.Link}");
      await dbService.InsertAnnouncementAsync(ann.Department, ann.DepartmentShortName, ann.Link, ann.Title, ann.AddedDate);
      var subs = await dbService.GetSubscribersAsync(ann.DepartmentShortName);
      foreach (var chatId in subs)
      {
        await bot.SendMessage(chatId,
            $"Duyuru\nKimden: {ann.Department}\nTarih: {ann.AddedDate:dd.MM.yyyy}\n\n{ann.Title}\n\n{ann.Link}",
            cancellationToken: token);
      }
    }
  }
}
