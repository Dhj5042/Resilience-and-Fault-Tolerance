# Resilient .NET 8 Microservices with Polly

This project demonstrates how to build resilient microservices in .NET 8 using **Polly** for **retry** and **circuit breaker** patterns. It includes two services:

- **InventoryApi**: Simulates an inventory service that may fail intermittently.
- **ProductApi**: Consumes InventoryApi using `HttpClientFactory` with Polly policies to ensure resilience.

---

## 🧩 Domain Overview

This solution represents a simplified **e-commerce backend** scenario:

- `ProductApi` fetches product inventory details from `InventoryApi`.
- `InventoryApi` may randomly return errors to simulate instability.
- `ProductApi` uses Polly to handle transient failures through retries and circuit breaking.

---

## 🛠 Technologies Used

- .NET 8 Web API
- Polly (via `Microsoft.Extensions.Http.Polly`)
- Swagger / OpenAPI
- HttpClientFactory

---

## 🚀 Project Structure

/ResilientMicroservices
├── InventoryApi
│ └── Controllers
│ └── InventoryController.cs
├── ProductApi
│ ├── Controllers
│ │ └── InventoryController.cs
│ ├── Services
│ │ └── InventoryService.cs
│ └── Program.cs

## Resilience Behavior
	📉 Simulated Failures (InventoryApi)
	The InventoryApi randomly returns a 500 status code to simulate service instability.

	⚙️ Polly in ProductApi
	ProductApi uses two Polly policies:

	Retry: Retries 3 times with exponential backoff.

	Circuit Breaker: Opens the circuit after 2 failures and blocks calls for 20 seconds.