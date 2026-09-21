using Inventory.Api;
using Orders.Api;
using Payments.Api;

public sealed class WorkflowTests
{
    [Fact]
    public void Order_starts_in_inventory_reservation_pending_and_cannot_be_cancelled_after_confirmation()
    {
        var order = OrderEntity.Create(Guid.NewGuid(), [new OrderItem(Guid.NewGuid(), "Item", 2, 10m)], "usd");

        Assert.Equal(OrderStatus.InventoryReservationPending, order.Status);
        order.MarkInventoryReserved();
        order.Confirm();

        Assert.Throws<InvalidOperationException>(() => order.Cancel());
    }

    [Fact]
    public void Order_mark_inventory_reserved_transitions_to_payment_pending()
    {
        var order = OrderEntity.Create(Guid.NewGuid(), [new OrderItem(Guid.NewGuid(), "item", 1, 25m)], "usd");

        order.MarkInventoryReserved();

        Assert.Equal(OrderStatus.PaymentPending, order.Status);
    }

    [Fact]
    public void Inventory_item_update_tracks_available_and_reserved_quantities()
    {
        var item = new InventoryItemEntity { ProductId = Guid.NewGuid(), Available = 10, Reserved = 0 };

        item.Available -= 3;
        item.Reserved += 3;

        Assert.Equal(7, item.Available);
        Assert.Equal(3, item.Reserved);
    }

    [Fact]
    public void Order_failure_blocks_confirmation_and_sets_failed_state()
    {
        var order = OrderEntity.Create(Guid.NewGuid(), [new OrderItem(Guid.NewGuid(), "Item", 1, 15m)], "usd");

        order.Fail();

        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Throws<InvalidOperationException>(() => order.Confirm());
    }

    [Fact]
    public async Task Development_payment_provider_declines_only_explicit_decline_token()
    {
        var provider = new DevelopmentPaymentProvider();

        var success = await provider.ProcessAsync(25m, "token", CancellationToken.None);
        var failure = await provider.ProcessAsync(25m, "decline", CancellationToken.None);

        Assert.Equal((PaymentStatus.Completed, (string?)null), (success.Status, success.Reason));
        Assert.Equal((PaymentStatus.Failed, "Development provider declined the payment."), (failure.Status, failure.Reason));
    }
}