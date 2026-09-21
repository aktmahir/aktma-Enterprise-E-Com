using System.Text.Json;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379,abortConnect=false"));
var app = builder.Build();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cart" }));
app.MapGet("/api/v1/carts/{customerId:guid}", async (Guid customerId, IConnectionMultiplexer redis) => { var value = await redis.GetDatabase().StringGetAsync($"cart:{customerId}"); return value.HasValue ? Results.Ok(JsonSerializer.Deserialize<Cart>(value.ToString())) : Results.Ok(new Cart(customerId, [])); });
app.MapPost("/api/v1/carts/{customerId:guid}/items", async (Guid customerId, AddCartItemRequest request, IConnectionMultiplexer redis) =>
{
    if (request.Quantity <= 0) return Results.BadRequest(new { error = "Quantity must be positive." });
    var database = redis.GetDatabase(); var key = $"cart:{customerId}"; var value = await database.StringGetAsync(key); var cart = value.HasValue ? JsonSerializer.Deserialize<Cart>(value.ToString())! : new Cart(customerId, []); var items = cart.Items.ToList(); var index = items.FindIndex(item => item.ProductId == request.ProductId); var item = new CartItem(request.ProductId, request.Name, request.UnitPrice, request.Quantity); if (index >= 0) items[index] = items[index] with { Quantity = items[index].Quantity + request.Quantity }; else items.Add(item); var updated = cart with { Items = items }; await database.StringSetAsync(key, JsonSerializer.Serialize(updated), TimeSpan.FromDays(7)); return Results.Ok(updated);
});
app.MapDelete("/api/v1/carts/{customerId:guid}/items/{productId:guid}", async (Guid customerId, Guid productId, IConnectionMultiplexer redis) => { var database = redis.GetDatabase(); var key = $"cart:{customerId}"; var value = await database.StringGetAsync(key); if (!value.HasValue) return Results.NotFound(); var cart = JsonSerializer.Deserialize<Cart>(value.ToString())!; var updated = cart with { Items = cart.Items.Where(item => item.ProductId != productId).ToArray() }; await database.StringSetAsync(key, JsonSerializer.Serialize(updated), TimeSpan.FromDays(7)); return Results.Ok(updated); });
app.MapDelete("/api/v1/carts/{customerId:guid}", async (Guid customerId, IConnectionMultiplexer redis) => { await redis.GetDatabase().KeyDeleteAsync($"cart:{customerId}"); return Results.NoContent(); });
app.Run();
public sealed record AddCartItemRequest(Guid ProductId, string Name, decimal UnitPrice, int Quantity);
public sealed record Cart(Guid CustomerId, IReadOnlyList<CartItem> Items);
public sealed record CartItem(Guid ProductId, string Name, decimal UnitPrice, int Quantity);
public partial class Program;