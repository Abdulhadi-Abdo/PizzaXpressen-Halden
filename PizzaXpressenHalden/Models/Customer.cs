namespace PizzaXpressenHalden.Models;

public class Customer
{
	public int Id { get; set; }
	public string Phone { get; set; } = "";
	public string Name { get; set; } = "";
	public string? AddressText { get; set; }
	public string? PostNumber { get; set; }
	public string? PostPlace { get; set; }
	public string? Notes { get; set; }
	public System.DateTime CreatedAtUtc { get; set; } = System.DateTime.UtcNow;
	public System.DateTime UpdatedAtUtc { get; set; } = System.DateTime.UtcNow;
	public System.DateTime? LastOrderAtUtc { get; set; }
	public System.DateTime RetentionUntilUtc { get; set; } = System.DateTime.UtcNow.AddYears(1);
	public System.DateTime? DeletedAtUtc { get; set; }
}