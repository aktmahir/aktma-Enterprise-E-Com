using System.Text.Json;
using Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("OrderDb")));
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService(sp => new RabbitMqConsumer(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILogger<RabbitMqConsumer>>(), "orders.workflow", ["inventory.reserved", "inventory.reservation-failed", "payment.completed", "payment.failed"], async (type, data, correlation, message, token) => await OrderWorkflow.HandleAsync(sp, type, data, correlation, message, token)));
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.MigrateAsync();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "orders" }));
app.MapGet("/api/v1/orders/{id:guid}", async (Guid id, OrderDbContext db, CancellationToken cancellationToken) => await db.Orders.AsNoTracking().SingleOrDefaultAsync(order => order.Id == id, cancellationToken) is { } order ? Results.Ok(order.ToResponse()) : Results.NotFound());
app.MapGet("/api/v1/customers/{customerId:guid}/orders", async (Guid customerId, OrderDbContext db, CancellationToken cancellationToken) => Results.Ok((await db.Orders.AsNoTracking().Where(order => order.CustomerId == customerId).OrderByDescending(order => order.CreatedAt).ToListAsync(cancellationToken)).Select(order => order.ToResponse())));
app.MapPost("/api/v1/orders", async (CreateOrderRequest request, OrderDbContext db, CancellationToken cancellationToken) =>
{
    if (request.Items.Count == 0 || request.Items.Any(item => item.Quantity <= 0 || item.UnitPrice < 0)) return Results.BadRequest(new { error = "Order items must be valid and non-empty." });
    var order = OrderEntity.Create(request.CustomerId, request.Items, request.Currency); await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken); db.Orders.Add(order); db.Outbox.Add(OutboxEntity.Create(order.Id, "order.created", new OrderCreated(order.Id, order.CustomerId, order.Total, order.Currency, request.Items.Select(item => new InventoryReservationLine(item.ProductId, item.Quantity)).ToArray()))); await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return Results.Created($"/api/v1/orders/{order.Id}", order.ToResponse());
});
app.MapPost("/api/v1/orders/{id:guid}/cancel", async (Guid id, CancelOrderRequest request, OrderDbContext db, CancellationToken cancellationToken) =>
{
    var order = await db.Orders.SingleOrDefaultAsync(item => item.Id == id, cancellationToken); if (order is null) return Results.NotFound(); try { order.Cancel(); db.Outbox.Add(OutboxEntity.Create(id, "order.cancelled", new OrderCancelled(id, request.Reason))); await db.SaveChangesAsync(cancellationToken); return Results.Ok(order.ToResponse()); } catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
});
app.Run();

public sealed record CreateOrderRequest(Guid CustomerId, IReadOnlyList<OrderItem> Items, string Currency = "USD");
public sealed record CancelOrderRequest(string Reason);
public sealed record OrderItem(Guid ProductId, string Name, int Quantity, decimal UnitPrice);
public enum OrderStatus { Pending, InventoryReservationPending, InventoryReserved, PaymentPending, Confirmed, Processing, Shipped, Delivered, Cancelled, Failed }
public sealed class OrderEntity
{
    private OrderEntity() { }
    public Guid Id { get; private set; } public Guid CustomerId { get; private set; } public string ItemsJson { get; private set; } = "[]"; public decimal Total { get; private set; } public string Currency { get; private set; } = "USD"; public OrderStatus Status { get; private set; } public DateTimeOffset CreatedAt { get; private set; } public DateTimeOffset UpdatedAt { get; private set; }
    public static OrderEntity Create(Guid customerId, IReadOnlyList<OrderItem> items, string currency) => new() { Id = Guid.NewGuid(), CustomerId = customerId, ItemsJson = JsonSerializer.Serialize(items), Total = items.Sum(item => item.UnitPrice * item.Quantity), Currency = currency.ToUpperInvariant(), Status = OrderStatus.InventoryReservationPending, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
    public void Cancel() { if (Status is not (OrderStatus.Pending or OrderStatus.InventoryReservationPending or OrderStatus.PaymentPending)) throw new InvalidOperationException($"Order cannot be cancelled from {Status}."); Status = OrderStatus.Cancelled; UpdatedAt = DateTimeOffset.UtcNow; }
    public void MarkInventoryReserved() { if (Status is OrderStatus.PaymentPending or OrderStatus.InventoryReserved) return; if (Status != OrderStatus.InventoryReservationPending) throw new InvalidOperationException($"Invalid inventory transition from {Status}."); Status = OrderStatus.PaymentPending; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Confirm() { if (Status == OrderStatus.Confirmed) return; if (Status != OrderStatus.PaymentPending) throw new InvalidOperationException($"Invalid payment transition from {Status}."); Status = OrderStatus.Confirmed; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Fail() { if (Status is OrderStatus.Confirmed or OrderStatus.Cancelled or OrderStatus.Failed) return; Status = OrderStatus.Failed; UpdatedAt = DateTimeOffset.UtcNow; }
    public object ToResponse() => new { Id, CustomerId, Items = JsonSerializer.Deserialize<IReadOnlyList<OrderItem>>(ItemsJson), Total, Currency, Status, CreatedAt, UpdatedAt };
}
public sealed class OutboxEntity
{
    private OutboxEntity() { }
    public Guid Id { get; private set; } public Guid CorrelationId { get; private set; } public string RoutingKey { get; private set; } = string.Empty; public string EventType { get; private set; } = string.Empty; public string Payload { get; private set; } = string.Empty; public DateTimeOffset CreatedAt { get; private set; } public DateTimeOffset? PublishedAt { get; private set; } public int Attempts { get; private set; } public string? LastError { get; private set; }
    public static OutboxEntity Create<T>(Guid correlationId, string routingKey, T value) => new() { Id = Guid.NewGuid(), CorrelationId = correlationId, RoutingKey = routingKey, EventType = typeof(T).Name, Payload = JsonSerializer.Serialize(value), CreatedAt = DateTimeOffset.UtcNow };
    public void MarkPublished() => PublishedAt = DateTimeOffset.UtcNow; public void MarkFailed(Exception exception) { Attempts++; LastError = exception.Message; }
}
public sealed class InboxEntity { public Guid Id { get; set; } public string MessageId { get; set; } = string.Empty; public DateTimeOffset ProcessedAt { get; set; } }
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>(); public DbSet<OutboxEntity> Outbox => Set<OutboxEntity>(); public DbSet<InboxEntity> Inbox => Set<InboxEntity>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) { modelBuilder.Entity<OrderEntity>().ToTable("orders").HasKey(order => order.Id); modelBuilder.Entity<OrderEntity>().Property(order => order.Total).HasPrecision(18, 2); modelBuilder.Entity<OrderEntity>().HasIndex(order => new { order.CustomerId, order.CreatedAt }); modelBuilder.Entity<OutboxEntity>().ToTable("outbox").HasKey(item => item.Id); modelBuilder.Entity<OutboxEntity>().HasIndex(item => item.PublishedAt); modelBuilder.Entity<InboxEntity>().ToTable("inbox").HasKey(item => item.Id); modelBuilder.Entity<InboxEntity>().HasIndex(item => item.MessageId).IsUnique(); }
}
public sealed class OutboxPublisher(IServiceScopeFactory scopeFactory, IEventPublisher publisher, ILogger<OutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) { while (!stoppingToken.IsCancellationRequested) { await PublishBatchAsync(stoppingToken); await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); } }
    private async Task PublishBatchAsync(CancellationToken cancellationToken) { await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>(); var batch = await db.Outbox.Where(item => item.PublishedAt == null && item.Attempts < 10).OrderBy(item => item.CreatedAt).Take(20).ToListAsync(cancellationToken); foreach (var item in batch) { try { await PublishAsync(item, cancellationToken); item.MarkPublished(); await db.SaveChangesAsync(cancellationToken); } catch (Exception exception) { item.MarkFailed(exception); logger.LogError(exception, "Outbox publish failed for {EventType} {CorrelationId}", item.EventType, item.CorrelationId); await db.SaveChangesAsync(cancellationToken); } } }
    private Task PublishAsync(OutboxEntity item, CancellationToken cancellationToken) => item.EventType switch { nameof(OrderCreated) => publisher.PublishAsync("commerce.events", item.RoutingKey, JsonSerializer.Deserialize<OrderCreated>(item.Payload)!, item.CorrelationId, cancellationToken), nameof(OrderCancelled) => publisher.PublishAsync("commerce.events", item.RoutingKey, JsonSerializer.Deserialize<OrderCancelled>(item.Payload)!, item.CorrelationId, cancellationToken), nameof(PaymentRequested) => publisher.PublishAsync("commerce.events", item.RoutingKey, JsonSerializer.Deserialize<PaymentRequested>(item.Payload)!, item.CorrelationId, cancellationToken), nameof(OrderConfirmed) => publisher.PublishAsync("commerce.events", item.RoutingKey, JsonSerializer.Deserialize<OrderConfirmed>(item.Payload)!, item.CorrelationId, cancellationToken), _ => throw new InvalidOperationException($"Unsupported outbox event {item.EventType}") };
}
public static class OrderWorkflow
{
    public static async Task HandleAsync(IServiceProvider services, string type, JsonElement data, string? correlationId, string? messageId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>(); var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>(); if (messageId is not null && await db.Inbox.AnyAsync(item => item.MessageId == messageId, cancellationToken)) return;
        var orderId = data.GetProperty("orderId").GetGuid(); var order = await db.Orders.SingleAsync(item => item.Id == orderId, cancellationToken);
        if (type == nameof(InventoryReserved)) { order.MarkInventoryReserved(); db.Outbox.Add(OutboxEntity.Create(order.Id, "payment.requested", new PaymentRequested(order.Id, order.CustomerId, order.Total, order.Currency))); }
        else if (type is nameof(InventoryReservationFailed) or nameof(PaymentFailed)) order.Fail();
        else if (type == nameof(PaymentCompleted)) { order.Confirm(); db.Outbox.Add(OutboxEntity.Create(order.Id, "order.confirmed", new OrderConfirmed(order.Id))); }
        if (messageId is not null) db.Inbox.Add(new InboxEntity { Id = Guid.NewGuid(), MessageId = messageId, ProcessedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(cancellationToken);
    }
}
public partial class Program;