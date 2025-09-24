using Microsoft.AspNetCore.Mvc;

namespace InventoryApi.Controllers
{
    [Route("api/inventory")]
    [ApiController]
    public class InventoryController : ControllerBase
    {
        private static int _callCount = 0;

        // Deterministic failure for the first 3 calls (used to trigger retries/circuit breaker predictably)
        [HttpGet]
        public IActionResult Get()
        {
            _callCount++;
            if (_callCount <= 3)
            {
                return StatusCode(500, $"Simulated failure. Attempt {_callCount}");
            }

            var data = new[]
            {
                new { ProductId = 1, Quantity = 100 },
                new { ProductId = 2, Quantity = 50 }
            };
            return Ok(data);
        }

        // Randomized failure endpoint (used in blog Step 11 / failure simulation section)
        [HttpGet("random")]
        public IActionResult GetRandom()
        {
            var rand = Random.Shared.Next(1, 10);
            if (rand <= 4) return StatusCode(500, "Simulated 500 error");
            if (rand == 5) return StatusCode(429, "Simulated rate limit");
            return Ok(new { Sku = "WIDGET-001", Quantity = 100, Rand = rand });
        }
    }
}
