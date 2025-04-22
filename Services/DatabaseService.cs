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
            CREATE TABLE IF NOT EXISTS Users (ChatId INTEGER PRIMARY KEY, Username TEXT, FullName TEXT);
            CREATE TABLE IF NOT EXISTS  Departments (InsId INTEGER PRIMARY KEY, Name TEXT, 
                ShortName TEXT UNIQUE, Url TEXT);
            CREATE TABLE IF NOT EXISTS Subscriptions (ChatId INTEGER, InsId INTEGER, 
                PRIMARY KEY(ChatId, InsId),
                FOREIGN KEY(ChatId) REFERENCES Users(ChatId),
                FOREIGN KEY(InsId) REFERENCES Departments(InsId));
            CREATE TABLE IF NOT EXISTS Announcements (Id INTEGER PRIMARY KEY AUTOINCREMENT, 
                InsId INTEGER, Link TEXT UNIQUE, Title TEXT, AddedDate DATETIME,
                FOREIGN KEY(InsId) REFERENCES Departments(InsId));
        ";
        cmd.ExecuteNonQuery();
    }

    public async Task<bool> AddDepartmentAsync(Department dep)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT OR IGNORE INTO Departments (Name, ShortName, Url, InsId) " +
            "VALUES (@name, @shortName, @url, @insId);";
        cmd.Parameters.AddWithValue("@name", dep.Name);
        cmd.Parameters.AddWithValue("@shortName", dep.ShortName);
        cmd.Parameters.AddWithValue("@url", dep.Url);
        cmd.Parameters.AddWithValue("@insId", dep.InsId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<Department>> GetDepartmentsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
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

    public async Task<bool> AddUserAsync(
        long chatId,
        string? username,
        string fullName)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT OR IGNORE INTO Users (ChatId, Username, FullName) " +
            "VALUES (@chatId, @username, @fullName);";
        cmd.Parameters.AddWithValue("@chatId", chatId);
        cmd.Parameters.AddWithValue("@username", username ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@fullName", fullName);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> AddSubscriptionAsync(
        long chatId,
        int insId,
        string? username,
        string fullName)
    {
        await AddUserAsync(chatId, username, fullName);
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT OR IGNORE INTO Subscriptions (ChatId, InsId) " +
            "VALUES (@chatId, @insId);";
        cmd.Parameters.AddWithValue("@chatId", chatId);
        cmd.Parameters.AddWithValue("@insId", insId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> RemoveSubscriptionAsync(long chatId, int insId)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "DELETE FROM Subscriptions WHERE ChatId=@chatId AND InsId=@insId;";
        cmd.Parameters.AddWithValue("@chatId", chatId);
        cmd.Parameters.AddWithValue("@insId", insId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<string>> GetUserSubscriptionsAsync(long chatId)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT d.ShortName FROM Subscriptions s " +
            "JOIN Departments d ON s.InsId=d.InsId " +
            "WHERE s.ChatId=@chatId;";
        cmd.Parameters.AddWithValue("@chatId", chatId);
        var list = new List<string>();
        using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) list.Add(rdr.GetString(0));
        return list;
    }

    public async Task<List<Subscription>> GetSubscriptionsAsync(long chatId)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT InsId FROM Subscriptions WHERE ChatId=@chatId;";
        cmd.Parameters.AddWithValue("@chatId", chatId);
        var list = new List<Subscription>();
        using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new Subscription
            {
                ChatId = chatId,
                InsId = rdr.GetInt32(0)
            });
        }
        return list;
    }

    public async Task<List<long>> GetSubscribersAsync(int insId)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ChatId FROM Subscriptions WHERE InsId=@insId;";
        cmd.Parameters.AddWithValue("@insId", insId);
        var list = new List<long>();
        using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            list.Add(rdr.GetInt64(0));
        return list;
    }

    public async Task<bool> AnnouncementExistsAsync(string link)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Announcements WHERE Link=@link;";
        cmd.Parameters.AddWithValue("@link", link);
        var result = await cmd.ExecuteScalarAsync();
        var count = result != null ? Convert.ToInt64(result) : 0;
        return count > 0;
    }

    public async Task InsertAnnouncementAsync(int insId, string link, string title)
    {
        await InsertAnnouncementAsync(insId, link, title, DateTime.Now);
    }

    public async Task InsertAnnouncementAsync(
        int insId,
        string link,
        string title,
        DateTime addedDate)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Announcements (InsId, Link, Title, AddedDate) " +
            "VALUES (@insId, @link, @title, @addedDate);";
        cmd.Parameters.AddWithValue("@insId", insId);
        cmd.Parameters.AddWithValue("@link", link);
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@addedDate", addedDate);
        await cmd.ExecuteNonQueryAsync();
    }
}