namespace Services.Interfaces
{
    public interface IPeriodicFetchService
    {
        void Start();
        Task StopAsync();
    }
}
