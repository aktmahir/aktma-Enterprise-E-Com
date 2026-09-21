var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
var app = builder.Build();
app.MapOpenApi();
app.Use(async (context, next) => { if (!context.Request.Headers.TryGetValue("X-Correlation-Id", out var correlation)) { correlation = Guid.NewGuid().ToString(); context.Request.Headers["X-Correlation-Id"] = correlation; } context.Response.Headers["X-Correlation-Id"] = correlation.ToString(); await next(); });
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "gateway" }));
app.MapGet("/api/v1/catalog/products", async (IHttpClientFactory clients, IConfiguration configuration, string? search, string? category, int? page, int? pageSize, CancellationToken cancellationToken) => { var baseUrl = configuration["Services:Catalog"] ?? "http://localhost:5013"; var query = new QueryString(); if (!string.IsNullOrWhiteSpace(search)) query = query.Add("search", search); if (!string.IsNullOrWhiteSpace(category)) query = query.Add("category", category); if (page.HasValue) query = query.Add("page", page.Value.ToString()); if (pageSize.HasValue) query = query.Add("pageSize", pageSize.Value.ToString()); var client = clients.CreateClient(); return Results.Content(await client.GetStringAsync($"{baseUrl}/api/v1/products{query}", cancellationToken), "application/json"); });
app.MapGet("/api/v1/identity/health", async (IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken) => Results.Content(await clients.CreateClient().GetStringAsync($"{configuration["Services:Identity"] ?? "http://localhost:5021"}/health", cancellationToken), "application/json"));
app.MapPost("/api/v1/auth/register", (HttpRequest request, IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken) => ProxyAsync(request, clients, configuration["Services:Identity"] ?? "http://localhost:5129", "/api/v1/auth/register", cancellationToken));
app.MapPost("/api/v1/auth/login", (HttpRequest request, IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken) => ProxyAsync(request, clients, configuration["Services:Identity"] ?? "http://localhost:5129", "/api/v1/auth/login", cancellationToken));
app.MapPost("/api/v1/auth/refresh", (HttpRequest request, IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken) => ProxyAsync(request, clients, configuration["Services:Identity"] ?? "http://localhost:5129", "/api/v1/auth/refresh", cancellationToken));
app.MapPost("/api/v1/orders", async (HttpRequest request, IHttpClientFactory clients, IConfiguration configuration, CancellationToken cancellationToken) =>
{
	var baseUrl = configuration["Services:Orders"] ?? "http://localhost:5213";
	using var content = new StreamContent(request.Body);
	content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType ?? "application/json");
	var response = await clients.CreateClient().PostAsync($"{baseUrl}/api/v1/orders", content, cancellationToken);
	return Results.Content(await response.Content.ReadAsStringAsync(cancellationToken), "application/json", statusCode: (int)response.StatusCode);
});
app.Run();
static async Task<IResult> ProxyAsync(HttpRequest request, IHttpClientFactory clients, string baseUrl, string path, CancellationToken cancellationToken)
{
	using var content = new StreamContent(request.Body); content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType ?? "application/json"); var response = await clients.CreateClient().PostAsync($"{baseUrl}{path}", content, cancellationToken); return Results.Content(await response.Content.ReadAsStringAsync(cancellationToken), "application/json", statusCode: (int)response.StatusCode);
}
public partial class Program;