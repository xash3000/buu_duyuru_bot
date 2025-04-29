using Microsoft.Extensions.DependencyInjection;
using Services;
using Services.Interfaces;
using Models;

string apiToken = Environment.GetEnvironmentVariable("BUU_DUYURU_BOT_API_TOKEN") ?? string.Empty;
if (string.IsNullOrEmpty(apiToken))
{
  Console.WriteLine("Error: BUU_DUYURU_BOT_API_TOKEN environment variable not set.");
  return;
}

var serviceProvider = new ServiceCollection()
    .AddSingleton<IDatabaseService, DatabaseService>()
    .AddSingleton<IScraperService, ScraperService>()
    .AddSingleton<ICommandHandler>(sp =>
    {
      var dbService = sp.GetRequiredService<IDatabaseService>();
      var departments = Task.Run(() => dbService.GetDepartmentsAsync()).GetAwaiter().GetResult();
      return new CommandHandler(dbService, departments);
    })
    .AddSingleton<ICallbackHandler, CallbackHandler>()
    .AddSingleton<IBotService>(sp =>
    {
      var commandHandler = sp.GetRequiredService<ICommandHandler>();
      var callbackHandler = sp.GetRequiredService<ICallbackHandler>();
      return new BotService(apiToken, commandHandler, callbackHandler);
    })
    .AddSingleton<IPeriodicFetchService>(sp =>
    {
      var botService = sp.GetRequiredService<IBotService>();
      var scraperService = sp.GetRequiredService<IScraperService>();
      var dbService = sp.GetRequiredService<IDatabaseService>();
      var departments = Task.Run(() => dbService.GetDepartmentsAsync()).GetAwaiter().GetResult();
      return new PeriodicFetchService(botService, scraperService, dbService, departments);
    })
    .AddSingleton<List<Department>>(sp =>
    {
      var dbService = sp.GetRequiredService<IDatabaseService>();
      return Task.Run(() => dbService.GetDepartmentsAsync()).GetAwaiter().GetResult();
    })
    .BuildServiceProvider();

var dbService = serviceProvider.GetRequiredService<IDatabaseService>();
var botService = serviceProvider.GetRequiredService<IBotService>();
var periodicFetchService = serviceProvider.GetRequiredService<IPeriodicFetchService>();

try
{
  dbService.InitializeDatabase();

  await botService.StartAsync();
  periodicFetchService.Start();

  Console.WriteLine("Application started. Press CTRL+C to exit.");
  await Task.Delay(Timeout.Infinite, botService.GetCancellationToken());

  Console.WriteLine("Shutting down...");
  botService.Stop();
  await periodicFetchService.StopAsync();
  Console.WriteLine("Application stopped.");
}
catch (Exception ex)
{
  Console.WriteLine($"Unhandled exception during startup or main execution: {ex}");
}