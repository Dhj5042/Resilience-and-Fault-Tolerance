using Polly.CircuitBreaker;

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
                // Main inventory endpoint (deterministic first 3 failures on provider side)
                var response = await _httpClient.GetAsync("/api/inventory");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Inventory API returned non-success status: {Status}", response.StatusCode);
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogWarning(ex, "Circuit open - returning degraded response");
                return "Service unavailable due to open circuit. Please try again later.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while calling Inventory API.");
                return $"Error: {ex.Message}";
            }
        }

        // Optional: method to consume random failure simulation endpoint
        public async Task<string> GetInventoryRandomAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/inventory/random", cancellationToken);
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling randomized inventory endpoint");
                return $"Error: {ex.Message}";
            }
        }
    }
}
