using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Messaging;

public sealed record MessageEnvelope<T>(Guid MessageId, Guid CorrelationId, string EventType, int Version, DateTimeOffset OccurredAt, T Data);
public sealed record OrderCreated(Guid OrderId, Guid CustomerId, decimal Total, string Currency, IReadOnlyList<InventoryReservationLine>? Items = null);
public sealed record InventoryReservationRequested(Guid OrderId, Guid CustomerId, IReadOnlyList<InventoryReservationLine> Items);
public sealed record InventoryReservationLine(Guid ProductId, int Quantity);
public sealed record InventoryReserved(Guid OrderId, Guid ReservationId);
public sealed record InventoryReservationFailed(Guid OrderId, string Reason);
public sealed record PaymentRequested(Guid OrderId, Guid CustomerId, decimal Amount, string Currency);
public sealed record PaymentCompleted(Guid OrderId, Guid PaymentId);
public sealed record PaymentFailed(Guid OrderId, string Reason);
public sealed record InventoryReleaseRequested(Guid OrderId, Guid ReservationId);
public sealed record InventoryReleased(Guid OrderId, Guid ReservationId);
public sealed record OrderConfirmed(Guid OrderId);
public sealed record OrderCancelled(Guid OrderId, string Reason);

public interface IEventPublisher
{
    Task PublishAsync<T>(string exchange, string routingKey, T data, Guid correlationId, CancellationToken cancellationToken = default);
}

public sealed class RabbitMqEventPublisher(IConfiguration configuration, ILogger<RabbitMqEventPublisher> logger) : IEventPublisher, IAsyncDisposable
{
    private IConnection? connection;
    private IChannel? channel;

    public async Task PublishAsync<T>(string exchange, string routingKey, T data, Guid correlationId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        var envelope = new MessageEnvelope<T>(Guid.NewGuid(), correlationId, typeof(T).Name, 1, DateTimeOffset.UtcNow, data);
        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var activeChannel = channel ?? throw new InvalidOperationException("RabbitMQ channel is unavailable after connecting.");
        await activeChannel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        var properties = new BasicProperties { Persistent = true, MessageId = envelope.MessageId.ToString(), CorrelationId = correlationId.ToString(), Type = envelope.EventType, ContentType = "application/json" };
        await activeChannel.BasicPublishAsync(exchange, routingKey, mandatory: false, properties, body, cancellationToken);
        logger.LogInformation("Published {EventType} {MessageId} with correlation {CorrelationId}", envelope.EventType, envelope.MessageId, correlationId);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (channel is not null) return;
        var factory = new ConnectionFactory { HostName = configuration["RabbitMQ:Host"] ?? "localhost", UserName = configuration["RabbitMQ:Username"] ?? "ecommerce", Password = configuration["RabbitMQ:Password"] ?? "ecommerce_dev_only" };
        connection = await factory.CreateConnectionAsync(cancellationToken);
        channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync() { if (channel is not null) await channel.DisposeAsync(); if (connection is not null) await connection.DisposeAsync(); }
}