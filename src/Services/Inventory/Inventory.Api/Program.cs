using System.Text.Json;
using Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("InventoryDb")));
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddHostedService<InventoryOutboxPublisher>();
builder.Services.AddHostedService(sp => new RabbitMqConsumer(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILogger<RabbitMqConsumer>>(), "inventory.workflow", ["order.created", "inventory.release-requested"], async (type, data, correlation, message, token) => await InventoryWorkflow.HandleAsync(sp, type, data, correlation, message, token)));
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "inventory" }));
app.MapGet("/api/v1/inventory/{productId:guid}", async (Guid productId, InventoryDbContext db, CancellationToken cancellationToken) => await db.Items.AsNoTracking().SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken) is { } item ? Results.Ok(item) : Results.NotFound());
app.MapPost("/api/v1/inventory/{productId:guid}/adjust", async (Guid productId, AdjustInventoryRequest request, InventoryDbContext db, CancellationToken cancellationToken) => { if (request.QuantityDelta == 0) return Results.BadRequest(new { error = "Quantity delta cannot be zero." }); var item = await db.Items.SingleOrDefaultAsync(value => value.ProductId == productId, cancellationToken); if (item is null) { item = new InventoryItemEntity { ProductId = productId, Available = request.QuantityDelta }; if (item.Available < 0) return Results.Conflict(new { error = "Inventory cannot become negative." }); db.Items.Add(item); } else { item.Available += request.QuantityDelta; if (item.Available < 0) return Results.Conflict(new { error = "Inventory cannot become negative." }); } await db.SaveChangesAsync(cancellationToken); return Results.Ok(item); });
app.Run();
public sealed record AdjustInventoryRequest(int QuantityDelta);
public sealed class InventoryItemEntity { public Guid ProductId { get; set; } public int Available { get; set; } public int Reserved { get; set; } public uint Version { get; set; } }
public sealed class ReservationEntity { public Guid Id { get; set; } public Guid OrderId { get; set; } public string ItemsJson { get; set; } = "[]"; public string Status { get; set; } = "Reserved"; }
public sealed class InventoryInbox { public Guid Id { get; set; } public string MessageId { get; set; } = string.Empty; public DateTimeOffset ProcessedAt { get; set; } }
public sealed class InventoryOutbox { public Guid Id { get; set; } public Guid CorrelationId { get; set; } public string EventType { get; set; } = string.Empty; public string RoutingKey { get; set; } = string.Empty; public string Payload { get; set; } = string.Empty; public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset? PublishedAt { get; set; } public int Attempts { get; set; } public string? LastError { get; set; } }
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryItemEntity> Items => Set<InventoryItemEntity>(); public DbSet<ReservationEntity> Reservations => Set<ReservationEntity>(); public DbSet<InventoryInbox> Inbox => Set<InventoryInbox>(); public DbSet<InventoryOutbox> Outbox => Set<InventoryOutbox>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) { modelBuilder.Entity<InventoryItemEntity>().ToTable("inventory_items").HasKey(item => item.ProductId); modelBuilder.Entity<InventoryItemEntity>().Property(item => item.Version).IsRowVersion(); modelBuilder.Entity<ReservationEntity>().ToTable("reservations").HasKey(item => item.Id); modelBuilder.Entity<ReservationEntity>().HasIndex(item => item.OrderId).IsUnique(); modelBuilder.Entity<InventoryInbox>().ToTable("inbox").HasKey(item => item.Id); modelBuilder.Entity<InventoryInbox>().HasIndex(item => item.MessageId).IsUnique(); modelBuilder.Entity<InventoryOutbox>().ToTable("outbox").HasKey(item => item.Id); modelBuilder.Entity<InventoryOutbox>().HasIndex(item => item.PublishedAt); }
}
public static class InventoryWorkflow
{
    public static async Task HandleAsync(IServiceProvider services, string type, JsonElement data, string? correlationId, string? messageId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>(); if (messageId is not null && await db.Inbox.AnyAsync(item => item.MessageId == messageId, cancellationToken)) return;
        var orderId = data.GetProperty("orderId").GetGuid(); var existing = await db.Reservations.SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (type == nameof(OrderCreated) && existing is null)
        {
            var lines = data.GetProperty("items").Deserialize<IReadOnlyList<InventoryReservationLine>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? []; var available = await db.Items.Where(item => lines.Select(line => line.ProductId).Contains(item.ProductId)).ToDictionaryAsync(item => item.ProductId, cancellationToken); if (lines.Any(line => line.Quantity <= 0 || !available.TryGetValue(line.ProductId, out var stock) || stock.Available < line.Quantity)) db.Outbox.Add(CreateOutbox(orderId, "inventory.reservation-failed", new InventoryReservationFailed(orderId, "Insufficient inventory")));
            else { foreach (var line in lines) { available[line.ProductId].Available -= line.Quantity; available[line.ProductId].Reserved += line.Quantity; } var reservation = new ReservationEntity { Id = Guid.NewGuid(), OrderId = orderId, ItemsJson = JsonSerializer.Serialize(lines) }; db.Reservations.Add(reservation); db.Outbox.Add(CreateOutbox(orderId, "inventory.reserved", new InventoryReserved(orderId, reservation.Id))); }
        }
        else if (type == nameof(InventoryReleaseRequested) && existing is { Status: "Reserved" }) { var lines = JsonSerializer.Deserialize<IReadOnlyList<InventoryReservationLine>>(existing.ItemsJson) ?? []; foreach (var line in lines) { var stock = await db.Items.SingleAsync(item => item.ProductId == line.ProductId, cancellationToken); stock.Available += line.Quantity; stock.Reserved -= line.Quantity; } existing.Status = "Released"; db.Outbox.Add(CreateOutbox(orderId, "inventory.released", new InventoryReleased(orderId, existing.Id))); }
        if (messageId is not null) db.Inbox.Add(new InventoryInbox { Id = Guid.NewGuid(), MessageId = messageId, ProcessedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(cancellationToken);
    }

    private static InventoryOutbox CreateOutbox<T>(Guid correlationId, string routingKey, T payload) => new() { Id = Guid.NewGuid(), CorrelationId = correlationId, RoutingKey = routingKey, EventType = typeof(T).Name, Payload = JsonSerializer.Serialize(payload), CreatedAt = DateTimeOffset.UtcNow };
}
public sealed class InventoryOutboxPublisher(IServiceScopeFactory scopeFactory, IEventPublisher publisher, ILogger<InventoryOutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) { while (!stoppingToken.IsCancellationRequested) { await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>(); var rows = await db.Outbox.Where(item => item.PublishedAt == null && item.Attempts < 10).OrderBy(item => item.CreatedAt).Take(20).ToListAsync(stoppingToken); foreach (var row in rows) { try { await PublishAsync(row, stoppingToken); row.PublishedAt = DateTimeOffset.UtcNow; } catch (Exception exception) { row.Attempts++; row.LastError = exception.Message; logger.LogError(exception, "Inventory outbox publish failed for {CorrelationId}", row.CorrelationId); } await db.SaveChangesAsync(stoppingToken); } await Task.Delay(1000, stoppingToken); } }
    private Task PublishAsync(InventoryOutbox row, CancellationToken token) => row.EventType switch { nameof(InventoryReserved) => publisher.PublishAsync("commerce.events", row.RoutingKey, JsonSerializer.Deserialize<InventoryReserved>(row.Payload)!, row.CorrelationId, token), nameof(InventoryReservationFailed) => publisher.PublishAsync("commerce.events", row.RoutingKey, JsonSerializer.Deserialize<InventoryReservationFailed>(row.Payload)!, row.CorrelationId, token), nameof(InventoryReleased) => publisher.PublishAsync("commerce.events", row.RoutingKey, JsonSerializer.Deserialize<InventoryReleased>(row.Payload)!, row.CorrelationId, token), _ => throw new InvalidOperationException($"Unsupported inventory event {row.EventType}") };
}
public partial class Program;