namespace ProductApi.Service
{
    public interface IInventoryService
    {
        Task<string> GetInventoryAsync();
    }
}
