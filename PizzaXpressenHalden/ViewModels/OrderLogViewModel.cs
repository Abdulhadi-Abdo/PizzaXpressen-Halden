using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PizzaXpressenHalden.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;

namespace PizzaXpressenHalden.ViewModels;

public partial class OrderLogViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    public ObservableCollection<OrderLogRow> Orders { get; } = new();
    public ICollectionView OrdersView { get; }

    [ObservableProperty]
    private OrderLogRow? selectedOrder;

    [ObservableProperty]
    private string searchText = "";

    public IAsyncRelayCommand RefreshCommand { get; }

    public OrderLogViewModel(AppDbContext db)
    {
        _db = db;

        OrdersView = CollectionViewSource.GetDefaultView(Orders);
        OrdersView.Filter = FilterOrder;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);

        _ = LoadAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        OrdersView.Refresh();
    }

    private bool FilterOrder(object obj)
    {
        if (obj is not OrderLogRow order)
            return false;

        var search = (SearchText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(search))
            return true;

        search = search.ToLowerInvariant();

        return (order.CustomerName ?? "").ToLowerInvariant().Contains(search)
            || (order.Phone ?? "").ToLowerInvariant().Contains(search)
            || order.ReceiptNumber.ToString(CultureInfo.InvariantCulture).Contains(search);
    }

    private async Task LoadAsync()
    {
        Orders.Clear();

        var list = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync();

        foreach (var order in list)
        {
            Orders.Add(new OrderLogRow
            {
                ReceiptNumber = order.Id,
                CustomerName = order.Customer?.Name ?? "",
                Phone = order.Customer?.Phone ?? "",
                Address = order.Customer?.AddressText ?? "",
                Type = order.DeliveryType.ToString(),
                TotalPrice = order.Items.Sum(i => i.UnitPrice * i.Quantity),
                CreatedAt = order.CreatedAtUtc.ToLocalTime()
            });
        }

        OrdersView.Refresh();
        SelectedOrder = Orders.FirstOrDefault();
    }

    public partial class OrderLogRow : ObservableObject
    {
        [ObservableProperty]
        private int receiptNumber;

        [ObservableProperty]
        private string customerName = "";

        [ObservableProperty]
        private string phone = "";

        [ObservableProperty]
        private string address = "";

        [ObservableProperty]
        private string type = "";

        [ObservableProperty]
        private decimal totalPrice;

        [ObservableProperty]
        private DateTime createdAt;
    }
}