namespace PizzaXpressenHalden.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public int? PizzaId { get; set; }

    public string ItemName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Note { get; set; }

    public string DisplayName => ItemName == "Kj.tillegg" ? "Levering (+85)" : ItemName;
}