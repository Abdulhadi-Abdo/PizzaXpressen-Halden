namespace PizzaXpressenHalden.Models;

public enum OrderStatus
{
    New = 0,
    InProgress = 1,
    Ready = 2,
    Delivered = 3,
    Cancelled = 4
}

public enum DeliveryType
{
    Pickup = 0,
    Delivery = 1
}
