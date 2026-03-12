namespace PizzaXpressenHalden.Models;

public class Pizza
{
    public int Id { get; set; }
    public int Nummer { get; set; }
    public string Navn { get; set; } = "";
    public string? Beskrivelse { get; set; }
    public decimal PrisMedium { get; set; }
    public decimal PrisLarge { get; set; }
    public decimal PrisGlutenfri { get; set; }
    public bool IsActive { get; set; } = true;
    public System.Collections.Generic.List<PizzaTopping> PizzaToppings { get; set; } = new();
}
