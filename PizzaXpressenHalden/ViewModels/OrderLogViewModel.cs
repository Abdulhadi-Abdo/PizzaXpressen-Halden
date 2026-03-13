// File: ViewModels/OrderLogViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PizzaXpressenHalden.ViewModels;

public partial class OrderLogViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    public ObservableCollection<OrderRow> Orders { get; } = new();

    public IRelayCommand RefreshCommand { get; }

    public OrderLogViewModel(AppDbContext db)
    {
        _db = db;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        _ = LoadAsync();
    }

    public record OrderRow(
        int Id,
        DateTime CreatedAt,
        string Customer,
        string Phone,
        string Type,
        int ItemCount,
        decimal Total
    );

    private async Task LoadAsync()
    {
        Orders.Clear();

        var list = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .OrderByDescending(o => o.Id)
            .Take(200)
            .ToListAsync();

        foreach (var o in list)
        {
            var total = o.Items.Sum(i => i.UnitPrice * i.Quantity);
            var itemCount = o.Items.Sum(i => i.Quantity);

            Orders.Add(new OrderRow(
                o.Id,
                o.CreatedAtUtc.ToLocalTime(),
                o.Customer?.Name ?? "-",
                o.Customer?.Phone ?? "-",
                o.DeliveryType.ToString(),
                itemCount,
                total
            ));
        }
    }
}