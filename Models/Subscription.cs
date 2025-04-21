namespace Models;

public class Subscription
{
    public long ChatId { get; set; }
    public string Department { get; set; } = string.Empty;
    public string? Username { get; set; } // Telegram username
}
