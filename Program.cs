using Telegram.Bot;

string API_TOKEN = Environment.GetEnvironmentVariable("BUU_DUYURU_BOT_API_TOKEN") ?? "";
var bot = new TelegramBotClient(API_TOKEN);
var me = await bot.GetMeAsync();
Console.WriteLine($"Hello, World! I am user {me.Id} and my name is {me.FirstName}.");