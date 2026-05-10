namespace PizzaXpressenHalden.Models;

public class Topping
{
    public int Id { get; set; }
    public string Navn { get; set; } = "";
    public string KjokkenNavn { get; set; } = "";
    public int Sortering { get; set; }
    public bool Justerbar { get; set; }
    public System.Collections.Generic.List<PizzaTopping> PizzaToppings { get; set; } = new();
}
