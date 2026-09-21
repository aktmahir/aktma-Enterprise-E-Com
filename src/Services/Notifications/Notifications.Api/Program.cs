using System.Text.Json;
using Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("NotificationDb")));
builder.Services.AddSingleton<INotificationProvider, DevelopmentNotificationProvider>();
builder.Services.AddHostedService(sp => new RabbitMqConsumer(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILogger<RabbitMqConsumer>>(), "notifications.workflow", ["order.confirmed", "order.cancelled", "payment.completed", "payment.failed", "order.shipped", "order.delivered"], async (type, data, correlation, message, token) => await NotificationWorkflow.HandleAsync(sp, type, data, correlation, message, token)));
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "notifications" }));
app.MapGet("/api/v1/notifications/{customerId:guid}", async (Guid customerId, NotificationDbContext db, CancellationToken cancellationToken) => Results.Ok(await db.Notifications.AsNoTracking().Where(item => item.CustomerId == customerId).OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken)));
app.Run();
public sealed class NotificationEntity { public Guid Id { get; set; } public Guid CustomerId { get; set; } public Guid OrderId { get; set; } public string EventType { get; set; } = string.Empty; public string Subject { get; set; } = string.Empty; public string Body { get; set; } = string.Empty; public DateTimeOffset CreatedAt { get; set; } }
public sealed class NotificationInbox { public Guid Id { get; set; } public string MessageId { get; set; } = string.Empty; public DateTimeOffset ProcessedAt { get; set; } }
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options) { public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>(); public DbSet<NotificationInbox> Inbox => Set<NotificationInbox>(); protected override void OnModelCreating(ModelBuilder modelBuilder) { modelBuilder.Entity<NotificationEntity>().ToTable("notifications").HasKey(item => item.Id); modelBuilder.Entity<NotificationEntity>().HasIndex(item => new { item.CustomerId, item.CreatedAt }); modelBuilder.Entity<NotificationInbox>().ToTable("inbox").HasKey(item => item.Id); modelBuilder.Entity<NotificationInbox>().HasIndex(item => item.MessageId).IsUnique(); } }
public interface INotificationProvider { Task SendAsync(NotificationEntity notification, CancellationToken cancellationToken); }
public sealed class DevelopmentNotificationProvider(ILogger<DevelopmentNotificationProvider> logger) : INotificationProvider { public Task SendAsync(NotificationEntity notification, CancellationToken cancellationToken) { logger.LogInformation("Notification {NotificationId} for order {OrderId}: {Subject}", notification.Id, notification.OrderId, notification.Subject); return Task.CompletedTask; } }
public static class NotificationWorkflow
{
    public static async Task HandleAsync(IServiceProvider services, string type, JsonElement data, string? correlationId, string? messageId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>(); var provider = scope.ServiceProvider.GetRequiredService<INotificationProvider>(); if (messageId is not null && await db.Inbox.AnyAsync(item => item.MessageId == messageId, cancellationToken)) return; var orderId = data.GetProperty("orderId").GetGuid(); var customerId = data.TryGetProperty("customerId", out var customer) ? customer.GetGuid() : Guid.Empty; var notification = new NotificationEntity { Id = Guid.NewGuid(), CustomerId = customerId, OrderId = orderId, EventType = type, Subject = type.Replace('.', ' '), Body = $"Order {orderId} event: {type}", CreatedAt = DateTimeOffset.UtcNow }; db.Notifications.Add(notification); if (messageId is not null) db.Inbox.Add(new NotificationInbox { Id = Guid.NewGuid(), MessageId = messageId, ProcessedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(cancellationToken); await provider.SendAsync(notification, cancellationToken);
    }
}
public sealed class NotificationDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<NotificationDbContext> { public NotificationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<NotificationDbContext>().UseNpgsql("Host=localhost;Port=5432;Database=notifications;Username=ecommerce;Password=ecommerce_dev_only").Options); }
public partial class Program;