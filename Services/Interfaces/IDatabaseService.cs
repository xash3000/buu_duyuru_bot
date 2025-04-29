using Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Interfaces
{
    public interface IDatabaseService
    {
        void InitializeDatabase();
        Task<bool> AddDepartmentAsync(Department dep);
        Task<List<Department>> GetDepartmentsAsync();
        Task<bool> AddUserAsync(long chatId, string? username, string fullName);
        Task<bool> AddSubscriptionAsync(long chatId, int insId, string? username, string fullName);
        Task<bool> RemoveSubscriptionAsync(long chatId, int insId);
        Task<List<string>> GetUserSubscriptionsAsync(long chatId);
        Task<List<Subscription>> GetSubscriptionsAsync(long chatId);
        Task<List<long>> GetSubscribersAsync(int insId);
        Task<bool> AnnouncementExistsAsync(string link);
        Task InsertAnnouncementAsync(int insId, string link, string title);
        Task InsertAnnouncementAsync(int insId, string link, string title, DateTime addedDate);
    }
}
