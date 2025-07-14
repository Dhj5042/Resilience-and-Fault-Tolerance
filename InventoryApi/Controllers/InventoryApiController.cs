using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace InventoryApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InventoryApiController : ControllerBase
    {
        private static int _callCount = 0;

        [HttpGet]
        public IActionResult Get()
        {
            _callCount++;

            // Simulate failure for first 3 calls
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
    }
}
