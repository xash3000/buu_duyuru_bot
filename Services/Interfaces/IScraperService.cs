using Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Interfaces
{
    public interface IScraperService
    {
        Task<List<Announcement>> FetchAnnouncementsAsync(List<Department> departments);
    }
}
