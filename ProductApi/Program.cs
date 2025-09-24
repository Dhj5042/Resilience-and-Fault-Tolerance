using Polly;
using Polly.Extensions.Http;
using Polly.CircuitBreaker;
using Polly.Timeout;
using ProductApi.Service;
using System.Net;
using System.Diagnostics; // For Activity tagging in observability examples

var builder = WebApplication.CreateBuilder(args);

// =============================================================
// Configuration-bound resilience options
// =============================================================
var resilienceSection = builder.Configuration.GetSection("Resilience");
var retrySettings = resilienceSection.GetSection("Retry");
var breakerSettings = resilienceSection.GetSection("CircuitBreaker");
var timeoutSettings = resilienceSection.GetSection("Timeout");

int retryMaxAttempts = retrySettings.GetValue<int>("MaxAttempts", 3);
int retryBaseDelaySeconds = retrySettings.GetValue<int>("BaseDelaySeconds", 2);
int breakerFailures = breakerSettings.GetValue<int>("FailuresBeforeBreak", 2);
int breakerBreakSeconds = breakerSettings.GetValue<int>("BreakSeconds", 20);
int timeoutSeconds = timeoutSettings.GetValue<int>("Seconds", 5);

Console.WriteLine($"[Resilience Config] Retry(MaxAttempts={retryMaxAttempts}, BaseDelay={retryBaseDelaySeconds}s) | Breaker(Failures={breakerFailures}, Break={breakerBreakSeconds}s) | Timeout({timeoutSeconds}s)");

// =============================================================
// Timeout Policy (innermost per attempt)
// =============================================================
var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(timeoutSeconds));

// =============================================================
// Retry Policy (Exponential Backoff + Jitter + 429)
// =============================================================
var jitter = new Random();
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()              // 5xx, 408, HttpRequestException
    .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests) // 429
    .WaitAndRetryAsync(
        retryCount: retryMaxAttempts,
        sleepDurationProvider: attempt =>
        {
            var baseDelay = TimeSpan.FromSeconds(retryBaseDelaySeconds * Math.Pow(2, attempt - 1));
            var jitterFactor = 1 + (jitter.NextDouble() * 0.2);
            return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * jitterFactor);
        },
        onRetry: (outcome, delay, attempt, ctx) =>
        {
            Console.WriteLine($"[Retry] Attempt={attempt} Delay={delay.TotalMilliseconds:N0}ms Status={(outcome.Result?.StatusCode.ToString() ?? "EXCEPTION")}");
            Activity.Current?.AddTag("resilience.retry.attempt", attempt);
            Activity.Current?.AddTag("resilience.retry.delay_ms", delay.TotalMilliseconds);
        });

// =============================================================
// Circuit Breaker Policy
// =============================================================
var circuitBreakerPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: breakerFailures,
        durationOfBreak: TimeSpan.FromSeconds(breakerBreakSeconds),
        onBreak: (outcome, span) =>
        {
            Console.WriteLine($"[CircuitBreak] OPEN for {span.TotalSeconds}s Reason={outcome.Result?.StatusCode}");
            Activity.Current?.AddTag("resilience.circuit.state", "open");
        },
        onReset: () =>
        {
            Console.WriteLine("[CircuitBreak] CLOSED (reset)");
            Activity.Current?.AddTag("resilience.circuit.state", "closed");
        },
        onHalfOpen: () =>
        {
            Console.WriteLine("[CircuitBreak] HALF-OPEN (probing)");
            Activity.Current?.AddTag("resilience.circuit.state", "half-open");
        });

// =============================================================
// Fallback Policy (outermost)
// =============================================================
var fallbackPolicy = Policy<HttpResponseMessage>
    .Handle<BrokenCircuitException>()
    .Or<TimeoutRejectedException>()
    .Or<HttpRequestException>()
    .OrResult(r => (int)r.StatusCode >= 500 || r.StatusCode == HttpStatusCode.TooManyRequests)
    .FallbackAsync(
        fallbackValue: new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("Degraded response from fallback.")
        },
        onFallbackAsync: (outcome, context) =>
        {
            Console.WriteLine("[Fallback] Triggered");
            Activity.Current?.AddTag("resilience.fallback.triggered", true);
            return Task.CompletedTask;
        });

// =============================================================
// Policy Composition Order
// =============================================================
var composedPolicy = Policy.WrapAsync(fallbackPolicy, circuitBreakerPolicy, retryPolicy, timeoutPolicy);

// Optional adaptive retry (not wired by default)
var adaptiveRetry = Policy<HttpResponseMessage>
    .HandleResult(r => r.StatusCode == HttpStatusCode.TooManyRequests || (int)r.StatusCode >= 500)
    .WaitAndRetryAsync(3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
        onRetry: (outcome, delay, attempt, ctx) =>
        {
            if (outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests &&
                outcome.Result.Headers.RetryAfter?.Delta is { } serverDelay)
            {
                Console.WriteLine($"[AdaptiveRetry] honoring Retry-After={serverDelay}");
            }
            Console.WriteLine($"[AdaptiveRetry] attempt={attempt} delay={delay} status={outcome.Result?.StatusCode}");
        });
// Example: builder.Services.AddHttpClient("RateLimitedClient").AddPolicyHandler(adaptiveRetry);

// =============================================================
// Per-Client Strategy Registrations
// =============================================================
builder.Services.AddHttpClient<IInventoryService, InventoryService>(client =>
{
    client.BaseAddress = new Uri("https://localhost:7168");
})
.AddPolicyHandler(composedPolicy);

var paymentTimeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds - 2)));
var paymentFallback = Policy<HttpResponseMessage>
    .Handle<Exception>()
    .OrResult(r => (int)r.StatusCode >= 500 || r.StatusCode == HttpStatusCode.TooManyRequests)
    .FallbackAsync(new HttpResponseMessage(HttpStatusCode.Accepted)
    {
        Content = new StringContent("Payment accepted for async processing (fallback).")
    }, onFallbackAsync: (o, c) =>
    {
        Console.WriteLine("[Payment Fallback] Triggered");
        return Task.CompletedTask;
    });
var paymentComposite = Policy.WrapAsync(paymentFallback, paymentTimeout);

builder.Services.AddHttpClient("PaymentClient").AddPolicyHandler(paymentComposite);

builder.Services.AddHttpClient("NotificationClient").AddPolicyHandler(retryPolicy);

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

app.MapGet("/test-inventory", async (IInventoryService svc) =>
{
    var result = await svc.GetInventoryAsync();
    return Results.Ok(result);
});

app.MapGet("/test-inventory-random", async (IHttpClientFactory f) =>
{
    var client = f.CreateClient(typeof(IInventoryService).Name);
    var response = await client.GetAsync("/api/inventory/random");
    return Results.Text(await response.Content.ReadAsStringAsync());
});

app.MapGet("/test-payment", async (IHttpClientFactory factory) =>
{
    var client = factory.CreateClient("PaymentClient");
    var response = await client.GetAsync("https://example.invalid/payment-sim");
    return Results.Text($"PaymentClient Status={(int)response.StatusCode} {response.StatusCode}");
});

app.MapGet("/test-notification", async (IHttpClientFactory factory) =>
{
    var client = factory.CreateClient("NotificationClient");
    var response = await client.GetAsync("https://example.invalid/notify-sim");
    return Results.Text($"NotificationClient Status={(int)response.StatusCode} {response.StatusCode}");
});

app.Run();
