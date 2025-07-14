using Polly;
using Polly.Extensions.Http;
using ProductApi.Service;

var builder = WebApplication.CreateBuilder(args);

// Polly Retry
IAsyncPolicy<HttpResponseMessage> retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, retryAttempt =>
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))); // Exponential backoff
// Polly Circuit Breaker
IAsyncPolicy<HttpResponseMessage> circuitBreakerPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5, // Allow 5 failures before opening
        durationOfBreak: TimeSpan.FromSeconds(10) // Break for 10 seconds
    );

builder.Services.AddHttpClient<IInventoryService, InventoryService>(client =>
{
    client.BaseAddress = new Uri("https://localhost:7168"); // Replace with actual InventoryApi URL
})
.AddPolicyHandler(retryPolicy)
.AddPolicyHandler(circuitBreakerPolicy);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
