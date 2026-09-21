using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Messaging;

public sealed class RabbitMqConsumer(
    IConfiguration configuration,
    ILogger<RabbitMqConsumer> logger,
    string queue,
    IReadOnlyList<string> routingKeys,
    Func<string, JsonElement, string?, string?, CancellationToken, Task> handler) : BackgroundService
{
    private const int MaximumAttempts = 3;
    private IConnection? connection;
    private IChannel? channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration["RabbitMQ:Host"] ?? "localhost",
            UserName = configuration["RabbitMQ:Username"] ?? "ecommerce",
            Password = configuration["RabbitMQ:Password"] ?? "ecommerce_dev_only"
        };
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                connection = await factory.CreateConnectionAsync(stoppingToken);
                channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.ExchangeDeclareAsync("commerce.events", ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
                await channel.ExchangeDeclareAsync("commerce.events.dlx", ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync($"{queue}.dlq", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                await channel.QueueBindAsync($"{queue}.dlq", "commerce.events.dlx", "#", cancellationToken: stoppingToken);
                foreach (var routingKey in routingKeys) await channel.QueueBindAsync(queue, "commerce.events", routingKey, cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, 1, false, stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, args) => await ProcessAsync(args, stoppingToken);
                await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RabbitMQ consumer {Queue} could not connect; retrying.", queue);
                if (channel is not null) await channel.DisposeAsync();
                if (connection is not null) await connection.DisposeAsync();
                channel = null;
                connection = null;
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(args.Body.ToArray());
            var root = document.RootElement;
            var eventType = root.GetProperty("eventType").GetString() ?? args.BasicProperties.Type ?? string.Empty;
            var data = root.GetProperty("data");
            var correlationId = root.TryGetProperty("correlationId", out var correlation) ? correlation.GetString() : args.BasicProperties.CorrelationId;
            await handler(eventType, data, correlationId, args.BasicProperties.MessageId, cancellationToken);
            await channel!.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception exception)
        {
            var attempts = GetAttempts(args.BasicProperties.Headers);
            logger.LogError(exception, "Message processing failed for {Queue}, attempt {Attempt}, message {MessageId}", queue, attempts, args.BasicProperties.MessageId);
            if (attempts >= MaximumAttempts)
            {
                await channel!.BasicPublishAsync("commerce.events.dlx", "failed", mandatory: false, new BasicProperties { Persistent = true, Headers = new Dictionary<string, object?> { ["x-failed-queue"] = queue, ["x-attempts"] = attempts } }, args.Body, cancellationToken);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
            }
            else
            {
                await channel!.BasicPublishAsync("commerce.events", args.RoutingKey, mandatory: false, new BasicProperties { Persistent = true, Headers = new Dictionary<string, object?> { ["x-attempts"] = attempts + 1 } }, args.Body, cancellationToken);
                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
            }
        }
    }

    private static int GetAttempts(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-attempts", out var value)) return 0;
        return value switch
        {
            byte[] bytes when int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var attempts) => attempts,
            int attempts => attempts,
            long attempts => (int)attempts,
            string attempts when int.TryParse(attempts, out var parsed) => parsed,
            _ => 0
        };
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (channel is not null) await channel.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}