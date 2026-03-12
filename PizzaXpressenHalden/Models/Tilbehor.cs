namespace PizzaXpressenHalden.Models;

public class Tilbehor
{
    public int Id { get; set; }
    public string Navn { get; set; } = "";
    public decimal Pris { get; set; }
    public bool IsActive { get; set; } = true;
}
