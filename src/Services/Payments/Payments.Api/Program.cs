using System.Text.Json;
using Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb")));
builder.Services.AddSingleton<IPaymentProvider, DevelopmentPaymentProvider>();
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddHostedService(sp => new RabbitMqConsumer(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILogger<RabbitMqConsumer>>(), "payments.workflow", ["payment.requested"], async (type, data, correlation, message, token) => await PaymentWorkflow.HandleAsync(sp, type, data, correlation, message, token)));
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "payments" }));
app.MapGet("/api/v1/payments/{orderId:guid}", async (Guid orderId, PaymentDbContext db, CancellationToken cancellationToken) => await db.Payments.AsNoTracking().SingleOrDefaultAsync(payment => payment.OrderId == orderId, cancellationToken) is { } payment ? Results.Ok(payment) : Results.NotFound());
app.MapPost("/api/v1/payments/{orderId:guid}/refund", async (Guid orderId, PaymentDbContext db, CancellationToken cancellationToken) => { var payment = await db.Payments.SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken); if (payment is null) return Results.NotFound(); payment.Status = PaymentStatus.Refunded; await db.SaveChangesAsync(cancellationToken); return Results.Ok(payment); });
app.Run();
public enum PaymentStatus { Pending, Completed, Failed, Refunded }
public sealed class PaymentEntity { public Guid Id { get; set; } public Guid OrderId { get; set; } public decimal Amount { get; set; } public string Currency { get; set; } = "USD"; public PaymentStatus Status { get; set; } public string? FailureReason { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class PaymentInbox { public Guid Id { get; set; } public string MessageId { get; set; } = string.Empty; public DateTimeOffset ProcessedAt { get; set; } }
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options) { public DbSet<PaymentEntity> Payments => Set<PaymentEntity>(); public DbSet<PaymentInbox> Inbox => Set<PaymentInbox>(); protected override void OnModelCreating(ModelBuilder modelBuilder) { modelBuilder.Entity<PaymentEntity>().ToTable("payments").HasKey(item => item.Id); modelBuilder.Entity<PaymentEntity>().HasIndex(item => item.OrderId).IsUnique(); modelBuilder.Entity<PaymentEntity>().Property(item => item.Amount).HasPrecision(18, 2); modelBuilder.Entity<PaymentInbox>().ToTable("inbox").HasKey(item => item.Id); modelBuilder.Entity<PaymentInbox>().HasIndex(item => item.MessageId).IsUnique(); } }
public interface IPaymentProvider { Task<(PaymentStatus Status, string? Reason)> ProcessAsync(decimal amount, string token, CancellationToken cancellationToken); }
public sealed class DevelopmentPaymentProvider : IPaymentProvider { public Task<(PaymentStatus Status, string? Reason)> ProcessAsync(decimal amount, string token, CancellationToken cancellationToken) => Task.FromResult(token.Equals("decline", StringComparison.OrdinalIgnoreCase) ? (PaymentStatus.Failed, "Development provider declined the payment.") : (PaymentStatus.Completed, (string?)null)); }
public static class PaymentWorkflow
{
    public static async Task HandleAsync(IServiceProvider services, string type, JsonElement data, string? correlationId, string? messageId, CancellationToken cancellationToken)
    {
        if (type != nameof(PaymentRequested)) return; await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>(); var provider = scope.ServiceProvider.GetRequiredService<IPaymentProvider>(); var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>(); if (messageId is not null && await db.Inbox.AnyAsync(item => item.MessageId == messageId, cancellationToken)) return; var orderId = data.GetProperty("orderId").GetGuid(); if (await db.Payments.AnyAsync(item => item.OrderId == orderId, cancellationToken)) return; var amount = data.GetProperty("amount").GetDecimal(); var result = await provider.ProcessAsync(amount, "success", cancellationToken); var payment = new PaymentEntity { Id = Guid.NewGuid(), OrderId = orderId, Amount = amount, Currency = data.GetProperty("currency").GetString() ?? "USD", Status = result.Status, FailureReason = result.Reason, CreatedAt = DateTimeOffset.UtcNow }; db.Payments.Add(payment); if (messageId is not null) db.Inbox.Add(new PaymentInbox { Id = Guid.NewGuid(), MessageId = messageId, ProcessedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(cancellationToken); if (result.Status == PaymentStatus.Completed) await publisher.PublishAsync("commerce.events", "payment.completed", new PaymentCompleted(orderId, payment.Id), orderId, cancellationToken); else await publisher.PublishAsync("commerce.events", "payment.failed", new PaymentFailed(orderId, result.Reason ?? "Payment failed"), orderId, cancellationToken);
    }
}
public sealed class PaymentDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<PaymentDbContext> { public PaymentDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql("Host=localhost;Port=5432;Database=payments;Username=ecommerce;Password=ecommerce_dev_only").Options); }
public partial class Program;