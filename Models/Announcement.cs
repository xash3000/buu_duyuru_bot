using System;

namespace Models;

public class Announcement
{
    public required string Department { get; set; }
    public required string DepartmentShortName { get; set; }
    public required string Link { get; set; }
    public required string Title { get; set; }
    public DateTime AddedDate { get; set; } // eklenme tarihi
}