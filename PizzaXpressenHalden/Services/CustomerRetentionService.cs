using PizzaXpressenHalden.Models;

namespace PizzaXpressenHalden;

public class CustomerRetentionService
{
    public bool CanDelete(Customer customer, System.DateTime utcNow, out string reason)
    {
        if (utcNow < customer.RetentionUntilUtc)
        {
            reason = $"Kunde kan ikke slettes før {customer.RetentionUntilUtc:dd.MM.yyyy}.";
            return false;
        }

        reason = "";
        return true;
    }

    public void ExtendRetentionIfNeeded(Customer customer)
    {
        if (customer.LastOrderAtUtc is null) return;
        var candidate = customer.LastOrderAtUtc.Value.AddYears(1);
        if (candidate > customer.RetentionUntilUtc) customer.RetentionUntilUtc = candidate;
    }
}
