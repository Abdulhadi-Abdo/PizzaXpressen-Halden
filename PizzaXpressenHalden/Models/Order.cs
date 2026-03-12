namespace PizzaXpressenHalden.Models;

public class Order
{
    public int Id { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public System.DateTime CreatedAtUtc { get; set; } = System.DateTime.UtcNow;
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Pickup;
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public string? Notes { get; set; }
    public System.Collections.Generic.List<OrderItem> Items { get; set; } = new();
}