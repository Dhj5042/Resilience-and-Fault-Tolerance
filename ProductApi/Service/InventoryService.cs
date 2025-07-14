using System.Net;

namespace ProductApi.Service
{
    public class InventoryService : IInventoryService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(HttpClient httpClient, ILogger<InventoryService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> GetInventoryAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/InventoryApi");

                if (response.StatusCode == HttpStatusCode.InternalServerError)
                {
                    _logger.LogWarning("Received 500 from Inventory API");
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Polly.CircuitBreaker.BrokenCircuitException ex)
            {
                _logger.LogError(ex, "Circuit is open. Inventory API calls are blocked.");
                return "Service unavailable due to open circuit. Please try again later.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while calling Inventory API.");
                return $"Error: {ex.Message}";
            }
        }

    }
}
