using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Models;
using Services.Interfaces;

namespace Services
{
    public class PeriodicFetchService : IPeriodicFetchService
    {
        private readonly IBotService _botService;
        private readonly IScraperService _scraperService;
        private readonly IDatabaseService _dbService;
        private readonly List<Department> _departments;
        private readonly TimeSpan _fetchInterval = TimeSpan.FromMinutes(10);
        private PeriodicTimer? _timer;
        private Task? _timerTask;
        private readonly CancellationTokenSource _cts = new();

        public PeriodicFetchService(IBotService botService, IScraperService scraperService, IDatabaseService dbService, List<Department> departments) // Inject interfaces
        {
            _botService = botService;
            _scraperService = scraperService;
            _dbService = dbService;
            _departments = departments;
        }

        /// <summary>
        /// Starts the periodic fetching process.
        /// </summary>
        public void Start()
        {
            if (_timerTask != null && !_timerTask.IsCompleted)
            {
                Console.WriteLine("Periodic fetch service already running.");
                return;
            }

            _timer = new PeriodicTimer(_fetchInterval);
            _timerTask = RunPeriodicFetchAsync(_cts.Token);
            Console.WriteLine($"Periodic fetch service started. Interval: {_fetchInterval.TotalMinutes} minutes.");
        }

        /// <summary>
        /// Stops the periodic fetching process.
        /// </summary>
        public async Task StopAsync()
        {
            if (_timerTask == null) return;

            _cts.Cancel();
            _timer?.Dispose();
            try
            {
                await _timerTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping periodic fetch service: {ex.Message}");
            }
            finally
            {
                _timerTask = null;
                Console.WriteLine("Periodic fetch service stopped.");
            }
        }

        /// <summary>
        /// Runs the main loop for periodic fetching and sending.
        /// </summary>
        private async Task RunPeriodicFetchAsync(CancellationToken token)
        {
            // Wait a bit for BotService to potentially initialize the client
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            await FetchAndSendAsync(token);

            try
            {
                if (_timer == null)
                {
                    Console.WriteLine("[Error] PeriodicTimer is not initialized in RunPeriodicFetchAsync.");
                    return;
                }
                while (await _timer.WaitForNextTickAsync(token))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Starting periodic fetch...");
                    await FetchAndSendAsync(token);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Periodic fetch finished.");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Periodic fetch loop cancelled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unhandled exception in periodic fetch loop: {ex}");
            }
        }

        /// <summary>
        /// Fetches new announcements and sends them to subscribed users asynchronously.
        /// </summary>
        private async Task FetchAndSendAsync(CancellationToken token)
        {
            try
            {
                var announcements = await _scraperService.FetchAnnouncementsAsync(_departments);
                int newAnnouncementsCount = 0;
                ITelegramBotClient botClient = _botService.GetClient();

                foreach (var ann in announcements)
                {
                    if (token.IsCancellationRequested) break;

                    if (!await _dbService.AnnouncementExistsAsync(ann.Link))
                    {
                        newAnnouncementsCount++;
                        Console.WriteLine($"[Periodic] New announcement found: {ann.Link}");
                        await _dbService.InsertAnnouncementAsync(ann.InsId, ann.Link, ann.Title, ann.AddedDate);

                        var department = _departments.FirstOrDefault(d => d.InsId == ann.InsId);
                        if (department == null)
                        {
                            Console.WriteLine($"[Warning] Department with InsId {ann.InsId} not found for announcement {ann.Link}. Skipping notification.");
                            continue;
                        }

                        var subs = await _dbService.GetSubscribersAsync(ann.InsId);
                        foreach (var chatId in subs)
                        {
                            if (token.IsCancellationRequested) break;
                            try
                            {
                                await botClient.SendMessage(
                                    chatId: chatId,
                                    text: "📢 Duyuru\n\n" +
                                          $"👤 Kimden: {department.Name}\n" +
                                          $"📅 Tarih: {ann.AddedDate:dd.MM.yyyy}\n\n" +
                                          "🗒 Konu:\n" +
                                          $"> {ann.Title}\n\n" +
                                          $"🔗 {ann.Link}",
                                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                                    cancellationToken: token
                                );
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error sending announcement {ann.Link} to chat {chatId}: {ex.Message}");
                            }
                        }
                    }
                }
                if (newAnnouncementsCount > 0)
                {
                    Console.WriteLine($"[Periodic] Processed {newAnnouncementsCount} new announcements.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during FetchAndSendAsync: {ex}");
            }
        }
    }
}
