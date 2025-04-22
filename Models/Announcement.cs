using System;

namespace Models;

public class Announcement
{
    public int Id { get; set; }
    public int InsId { get; set; }
    public required string Link { get; set; }
    public required string Title { get; set; }
    public DateTime AddedDate { get; set; }
}