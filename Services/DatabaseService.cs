namespace Services;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;

public class DatabaseService
{
    private const string DbPath = "buu_duyuru_bot.db";

    public void InitializeDatabase()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Departments (Name TEXT PRIMARY KEY, ShortName TEXT UNIQUE, Url TEXT, InsId INTEGER);
            CREATE TABLE IF NOT EXISTS Subscriptions (ChatId TEXT, Department TEXT, Username TEXT, PRIMARY KEY(ChatId, Department));
            CREATE TABLE IF NOT EXISTS Announcements (Id INTEGER PRIMARY KEY AUTOINCREMENT, Department TEXT, Link TEXT UNIQUE, Title TEXT, AddedDate DATETIME);
        ";
        cmd.ExecuteNonQuery();
    }

    public async Task<bool> AddDepartmentAsync(Department dep)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Departments (Name, ShortName, Url, InsId) VALUES (@name, @shortName, @url, @insId);";
        cmd.Parameters.AddWithValue("@name", dep.Name);
        cmd.Parameters.AddWithValue("@shortName", dep.ShortName);
        cmd.Parameters.AddWithValue("@url", dep.Url);
        cmd.Parameters.AddWithValue("@insId", dep.InsId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<Department>> GetDepartmentsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name, ShortName, Url, InsId FROM Departments;";
        var list = new List<Department>();
        using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new Department
            {
                Name = rdr.GetString(0),
                ShortName = rdr.GetString(1),
                Url = rdr.GetString(2),
                InsId = rdr.GetInt32(3)
            });
        }
        return list;
    }

    public async Task<bool> AddSubscriptionAsync(long chatId, string dept, string? username)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Subscriptions (ChatId, Department, Username) VALUES (@chatId, @dept, @username);";
        cmd.Parameters.AddWithValue("@chatId", chatId.ToString());
        cmd.Parameters.AddWithValue("@dept", dept);
        cmd.Parameters.AddWithValue("@username", username ?? (object)DBNull.Value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> RemoveSubscriptionAsync(long chatId, string dept)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Subscriptions WHERE ChatId=@chatId AND Department=@dept;";
        cmd.Parameters.AddWithValue("@chatId", chatId.ToString()); cmd.Parameters.AddWithValue("@dept", dept);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<string>> GetUserSubscriptionsAsync(long chatId)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Department FROM Subscriptions WHERE ChatId=@chatId;";
        cmd.Parameters.AddWithValue("@chatId", chatId.ToString());
        var list = new List<string>();
        using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) list.Add(rdr.GetString(0));
        return list;
    }

    public async Task<List<Subscription>> GetSubscriptionsAsync(long chatId)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Department, Username FROM Subscriptions WHERE ChatId=@chatId;";
        cmd.Parameters.AddWithValue("@chatId", chatId.ToString());
        var list = new List<Subscription>();
        using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new Subscription
            {
                ChatId = chatId,
                Department = rdr.GetString(0),
                Username = rdr.IsDBNull(1) ? null : rdr.GetString(1)
            });
        }
        return list;
    }

    public async Task<List<long>> GetSubscribersAsync(string dept)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ChatId FROM Subscriptions WHERE Department=@dept;";
        cmd.Parameters.AddWithValue("@dept", dept);
        var list = new List<long>();
        using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            if (long.TryParse(rdr.GetString(0), out var id))
                list.Add(id);
        return list;
    }

    public async Task<bool> AnnouncementExistsAsync(string link)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Announcements WHERE Link=@link;";
        cmd.Parameters.AddWithValue("@link", link);
        var result = await cmd.ExecuteScalarAsync();
        // Fix null unboxing issue by safely handling the result
        var count = result != null ? Convert.ToInt64(result) : 0;
        return count > 0;
    }

    public async Task InsertAnnouncementAsync(string dept, string link, string title)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Announcements (Department, Link, Title, AddedDate) VALUES (@dept, @link, @title, @addedDate);";
        cmd.Parameters.AddWithValue("@dept", dept);
        cmd.Parameters.AddWithValue("@link", link);
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@addedDate", DateTime.Now);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InsertAnnouncementAsync(string dept, string link, string title, DateTime addedDate)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}"); await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Announcements (Department, Link, Title, AddedDate) VALUES (@dept, @link, @title, @addedDate);";
        cmd.Parameters.AddWithValue("@dept", dept);
        cmd.Parameters.AddWithValue("@link", link);
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@addedDate", addedDate);
        await cmd.ExecuteNonQueryAsync();
    }
}