namespace Services;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Models;

public class DatabaseService
{
    private const string DbPath = "buu_duyuru_bot.db";

    /// <summary>
    /// Initializes the database by creating necessary tables if they don't exist.
    /// </summary>
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
                FOREIGN KEY(ChatId) REFERENCES Users(ChatId) ON DELETE CASCADE,
                FOREIGN KEY(InsId) REFERENCES Departments(InsId) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Announcements (Id INTEGER PRIMARY KEY AUTOINCREMENT, 
                InsId INTEGER, Link TEXT UNIQUE, Title TEXT, AddedDate DATETIME,
                FOREIGN KEY(InsId) REFERENCES Departments(InsId) ON DELETE CASCADE
            );
        ";

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Adds a department to the database asynchronously.
    /// </summary>
    /// <param name="dep">The department to add</param>
    /// <returns>True if the department was added, false if it already exists</returns>
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

    /// <summary>
    /// Gets all departments from the database asynchronously.
    /// </summary>
    /// <returns>A list of all departments</returns>
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

    /// <summary>
    /// Adds a user to the database asynchronously.
    /// </summary>
    /// <param name="chatId">The chat ID of the user</param>
    /// <param name="username">The username of the user</param>
    /// <param name="fullName">The full name of the user</param>
    /// <returns>True if the user was added, false if it already exists</returns>
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

    /// <summary>
    /// Adds a subscription for a user to a department asynchronously.
    /// </summary>
    /// <param name="chatId">The chat ID of the user</param>
    /// <param name="insId">The institution ID of the department</param>
    /// <param name="username">The username of the user</param>
    /// <param name="fullName">The full name of the user</param>
    /// <returns>True if the subscription was added, false if it already exists</returns>
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

    /// <summary>
    /// Removes a subscription for a user to a department asynchronously.
    /// </summary>
    /// <param name="chatId">The chat ID of the user</param>
    /// <param name="insId">The institution ID of the department</param>
    /// <returns>True if the subscription was removed, false if it didn't exist</returns>
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

    /// <summary>
    /// Gets the shortnames of subscribed departments for a user asynchronously.
    /// </summary>
    /// <param name="chatId">The chat ID of the user</param>
    /// <returns>A list of department shortnames the user is subscribed to</returns>
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

    /// <summary>
    /// Gets subscription objects for a user asynchronously.
    /// </summary>
    /// <param name="chatId">The chat ID of the user</param>
    /// <returns>A list of subscription objects for the user</returns>
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

    /// <summary>
    /// Gets the chat IDs of all subscribers to a department asynchronously.
    /// </summary>
    /// <param name="insId">The institution ID of the department</param>
    /// <returns>A list of chat IDs subscribed to the department</returns>
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

    /// <summary>
    /// Checks if an announcement already exists in the database asynchronously.
    /// </summary>
    /// <param name="link">The link of the announcement</param>
    /// <returns>True if the announcement exists, false otherwise</returns>
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

    /// <summary>
    /// Inserts an announcement into the database with the current date and time asynchronously.
    /// </summary>
    /// <param name="insId">The institution ID of the department</param>
    /// <param name="link">The link of the announcement</param>
    /// <param name="title">The title of the announcement</param>
    public async Task InsertAnnouncementAsync(int insId, string link, string title)
    {
        await InsertAnnouncementAsync(insId, link, title, DateTime.Now);
    }

    /// <summary>
    /// Inserts an announcement into the database with a specific date and time asynchronously.
    /// </summary>
    /// <param name="insId">The institution ID of the department</param>
    /// <param name="link">The link of the announcement</param>
    /// <param name="title">The title of the announcement</param>
    /// <param name="addedDate">The date and time the announcement was added</param>
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