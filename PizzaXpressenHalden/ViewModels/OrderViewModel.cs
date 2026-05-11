using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PizzaXpressenHalden.Models;
using PizzaXpressenHalden.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PizzaXpressenHalden.ViewModels;

public partial class OrderViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly PrintService _printer;
    private readonly AddressSuggestionService _address;

    private CancellationTokenSource? _phoneLookupCts;
    private CancellationTokenSource? _pizzaLookupCts;

    private bool _suppressPizzaLoad;
    private bool _suppressToppingEvents;
    private bool _isEditingPizza;
    private int _editingPizzaIndex = -1;
    private OrderItem? _editingOriginalLine;

    private readonly HashSet<string> _currentBaseToppings = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, string> _normalToppingValues = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, string> _half1ToppingValues = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, string> _half2ToppingValues = new(StringComparer.CurrentCultureIgnoreCase);

    private readonly Dictionary<string, decimal> _pricedToppingUnitPrices = new(StringComparer.CurrentCultureIgnoreCase);

    private decimal _driverFeePrice = 85m;

    [ObservableProperty] private bool isSplitPizza;
    [ObservableProperty] private bool isInfoPopupOpen;

    private Pizza? _splitPizza1;
    private Pizza? _splitPizza2;
    private int _activeHalf = 1;

    public ObservableCollection<OrderItem> Items { get; } = new();
    public ObservableCollection<string> AddressSuggestions { get; } = new();

    public ObservableCollection<string> DisplayIngredients { get; } = new();

    public ObservableCollection<string> ActiveAdds { get; } = new();
    public ObservableCollection<string> ActiveRemoves { get; } = new();

    public ObservableCollection<string> Half1Adds { get; } = new();
    public ObservableCollection<string> Half1Removes { get; } = new();
    public ObservableCollection<string> Half2Adds { get; } = new();
    public ObservableCollection<string> Half2Removes { get; } = new();

    public ObservableCollection<ToppingInput> ToppingInputs { get; } = new();

    public ObservableCollection<CatalogInput> LeftCatalogInputs { get; } = new();
    public ObservableCollection<CatalogInput> RightCatalogInputs { get; } = new();

    [ObservableProperty] private string pizzaNrText = "";
    [ObservableProperty] private string sizeText = "";
    [ObservableProperty] private int activePizzaQuantity = 1;

    [ObservableProperty] private string phone = "";
    [ObservableProperty] private string customerName = "";
    [ObservableProperty] private string addressText = "";
    [ObservableProperty] private string timeText = "";

    [ObservableProperty] private string orderNotesText = "";

    [ObservableProperty] private string deliveryInputText = "B";
    [ObservableProperty] private DeliveryType deliveryType = DeliveryType.Delivery;

    [ObservableProperty] private decimal total;
    [ObservableProperty] private OrderItem? selectedItem;

    [ObservableProperty] private Pizza? activePizza;
    [ObservableProperty] private string activePizzaTitle = "";
    [ObservableProperty] private decimal activePizzaPrice;

    [ObservableProperty] private string splitTitle = "";
    [ObservableProperty] private string activeHalfLabel = "";

    public IRelayCommand RemoveSelectedItemCommand { get; }
    public IRelayCommand SaveAndPrintCommand { get; }

    public IRelayCommand AddOrUpdatePizzaCommand { get; }
    public IRelayCommand CancelPizzaEditCommand { get; }
    public IRelayCommand EditSelectedPizzaCommand { get; }

    public IRelayCommand SelectHalf1Command { get; }
    public IRelayCommand SelectHalf2Command { get; }
    public IRelayCommand ToggleInfoPopupCommand { get; }

    public bool IsEditingPizza
    {
        get => _isEditingPizza;
        private set => SetProperty(ref _isEditingPizza, value);
    }

    public OrderViewModel(AppDbContext db, PrintService printer)
    {
        _db = db;
        _printer = printer;
        _address = new AddressSuggestionService();

        RemoveSelectedItemCommand = new RelayCommand(RemoveSelectedItem);
        SaveAndPrintCommand = new AsyncRelayCommand(SaveAndPrintAsync);

        AddOrUpdatePizzaCommand = new RelayCommand(AddOrUpdatePizza);
        CancelPizzaEditCommand = new RelayCommand(CancelPizzaEdit);
        EditSelectedPizzaCommand = new AsyncRelayCommand(EditSelectedPizzaAsync);

        SelectHalf1Command = new RelayCommand(() => SetActiveHalf(1));
        SelectHalf2Command = new RelayCommand(() => SetActiveHalf(2));
        ToggleInfoPopupCommand = new RelayCommand(() => IsInfoPopupOpen = !IsInfoPopupOpen);

        TimeText = DateTime.Now.AddMinutes(30).ToString("HH:mm");

        DeliveryType = DeliveryType.Delivery;
        DeliveryInputText = "B";
        SyncDriverFee();
        RecalcTotal();

        _ = LoadAsync();
    }

    public partial class ToppingInput : ObservableObject
    {
        public string Navn { get; }
        public bool IsPriced { get; }

        [ObservableProperty] private string input = "";
        [ObservableProperty] private string unitPriceText = "";

        public ToppingInput(string navn, bool isPriced, decimal unitPrice)
        {
            Navn = navn;
            IsPriced = isPriced;
            UnitPriceText = isPriced ? unitPrice.ToString("0.##", CultureInfo.InvariantCulture) : "";
        }
    }

    public partial class CatalogInput : ObservableObject
    {
        public string Navn { get; }
        public decimal Pris { get; }

        [ObservableProperty] private string quantityText = "";

        public CatalogInput(string navn, decimal pris)
        {
            Navn = navn;
            Pris = pris;
        }
    }

    partial void OnPhoneChanged(string value)
    {
        _phoneLookupCts?.Cancel();
        _phoneLookupCts = new CancellationTokenSource();
        var token = _phoneLookupCts.Token;
        _ = AutoLookupCustomerAsync(value, token);
    }

    partial void OnAddressTextChanged(string value)
    {
        AddressSuggestions.Clear();
        foreach (var s in _address.Suggest(value, 8))
            AddressSuggestions.Add(s);
    }

    partial void OnDeliveryInputTextChanged(string value)
    {
        var v = (value ?? "").Trim().ToUpperInvariant();
        if (v.Length == 0) return;

        var c = v[0];
        DeliveryType = c == 'H' ? DeliveryType.Pickup : DeliveryType.Delivery;

        SyncDriverFee();
        RecalcTotal();
    }

    partial void OnDeliveryTypeChanged(DeliveryType value)
    {
        DeliveryInputText = value == DeliveryType.Pickup ? "H" : "B";
        SyncDriverFee();
        RecalcTotal();
    }

    partial void OnPizzaNrTextChanged(string value)
    {
        if (_suppressPizzaLoad) return;

        _pizzaLookupCts?.Cancel();
        _pizzaLookupCts = new CancellationTokenSource();
        var token = _pizzaLookupCts.Token;
        _ = AutoLoadPizzaAsync(value, token);
    }

    partial void OnSizeTextChanged(string value)
    {
        RefreshActivePizzaPrice();
        RecalcTotal();
    }

    partial void OnActivePizzaQuantityChanged(int value)
    {
        RefreshActivePizzaPrice();
        RecalcTotal();
    }

    private static string NormalizePricedToppingName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var s = name.Trim().ToUpperInvariant();
        s = s.Replace("Ø", "O");
        s = s.Replace("Æ", "AE");
        s = s.Replace("Å", "A");
        s = s.Replace("-", "");
        s = s.Replace("_", "");
        s = s.Replace(" ", "");
        return s;
    }

    private static bool IsPricedSpecialToppingName(string? name)
    {
        var normalized = NormalizePricedToppingName(name);
        return normalized == "XOST" || normalized == "XKJOTT";
    }

    private bool TryGetPricedToppingUnitPrice(string? toppingName, out decimal unitPrice)
    {
        unitPrice = 0m;

        if (string.IsNullOrWhiteSpace(toppingName))
            return false;

        if (_pricedToppingUnitPrices.TryGetValue(toppingName, out unitPrice))
            return true;

        var normalized = NormalizePricedToppingName(toppingName);

        foreach (var kv in _pricedToppingUnitPrices)
        {
            if (NormalizePricedToppingName(kv.Key) == normalized)
            {
                unitPrice = kv.Value;
                return true;
            }
        }

        return false;
    }

    private async Task LoadAsync()
    {
        var toppings = await _db.Toppings
            .AsNoTracking()
            .OrderBy(x => x.Sortering)
            .Select(x => x.Navn)
            .ToListAsync();

        var til = await _db.Tilbehor
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Navn, x.Pris })
            .ToListAsync();

        var dr = await _db.Drinks
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Navn, x.Pris })
            .ToListAsync();

        var ex = await _db.Extras
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Navn, x.Pris })
            .ToListAsync();

        _pricedToppingUnitPrices.Clear();
        foreach (var x in ex)
        {
            if (IsPricedSpecialToppingName(x.Navn))
                _pricedToppingUnitPrices[x.Navn] = x.Pris;
        }

        ToppingInputs.Clear();
        foreach (var t in toppings)
        {
            var isPriced = IsPricedSpecialToppingName(t);
            TryGetPricedToppingUnitPrice(t, out var unitPrice);

            var ti = new ToppingInput(t, isPriced, unitPrice);
            ti.PropertyChanged += ToppingInputChanged;
            ToppingInputs.Add(ti);
        }

        var driverFee = ex.FirstOrDefault(x => x.Navn == "Kj.tillegg");
        _driverFeePrice = driverFee?.Pris ?? 85m;

        LeftCatalogInputs.Clear();
        RightCatalogInputs.Clear();

        foreach (var x in til.Concat(ex.Where(x =>
                     x.Navn != "Kj.tillegg" &&
                     !IsPricedSpecialToppingName(x.Navn))))
        {
            var ci = new CatalogInput(x.Navn, x.Pris);
            ci.PropertyChanged += CatalogInputChanged;
            LeftCatalogInputs.Add(ci);
        }

        foreach (var x in dr)
        {
            var ci = new CatalogInput(x.Navn, x.Pris);
            ci.PropertyChanged += CatalogInputChanged;
            RightCatalogInputs.Add(ci);
        }

        DeliveryType = DeliveryType.Delivery;
        DeliveryInputText = "B";
        SyncDriverFee();
        RecalcTotal();
    }

    private void ToppingInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressToppingEvents) return;
        if (sender is not ToppingInput ti) return;

        if (e.PropertyName == nameof(ToppingInput.UnitPriceText))
        {
            if (ti.IsPriced)
            {
                var rawPrice = (ti.UnitPriceText ?? "").Trim().Replace(",", ".");

                if (decimal.TryParse(rawPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && price >= 0)
                {
                    _pricedToppingUnitPrices[ti.Navn] = price;
                    RefreshActivePizzaPrice();
                    RecalcTotal();
                }
            }

            return;
        }

        if (e.PropertyName != nameof(ToppingInput.Input)) return;

        var store = GetCurrentToppingStore();
        var raw = (ti.Input ?? "").Trim();
        var baseHas = _currentBaseToppings.Contains(ti.Navn);

        if (baseHas)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Equals("x", StringComparison.CurrentCultureIgnoreCase))
                store.Remove(ti.Navn);
            else
                store[ti.Navn] = raw;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(raw))
                store.Remove(ti.Navn);
            else
                store[ti.Navn] = raw;
        }

        RefreshActivePizzaPrice();
        RecalcTotal();
    }

    private Dictionary<string, string> GetCurrentToppingStore()
    {
        if (IsSplitPizza)
            return _activeHalf == 1 ? _half1ToppingValues : _half2ToppingValues;

        return _normalToppingValues;
    }

    private static string BuildCustomToppingEncodedNote(Dictionary<string, string> values)
    {
        if (values.Count == 0)
            return "";

        return "CUSTOM: " + string.Join(";",
            values
                .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    private static string BuildCustomToppingDisplayNote(Dictionary<string, string> values)
    {
        if (values.Count == 0)
            return "";

        return string.Join(" | ",
            values
                .OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => $"{x.Key}={x.Value}"));
    }

    private static bool TryParseCustomToppingNote(string? note, out Dictionary<string, string> result)
    {
        result = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);

        if (string.IsNullOrWhiteSpace(note))
            return false;

        var trimmed = note.Trim();
        if (!trimmed.StartsWith("CUSTOM:", StringComparison.OrdinalIgnoreCase))
            return false;

        var body = trimmed.Substring(7).Trim();
        if (body.Length == 0)
            return true;

        var pairs = body.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;

            var key = Uri.UnescapeDataString(pair.Substring(0, idx).Trim());
            var value = Uri.UnescapeDataString(pair.Substring(idx + 1).Trim());

            if (key.Length == 0) continue;
            result[key] = value;
        }

        return true;
    }

    private static bool TryParseDisplayToppingNote(string? note, out Dictionary<string, string> result)
    {
        result = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);

        if (string.IsNullOrWhiteSpace(note))
            return false;

        var parts = note.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToList();

        bool found = false;

        foreach (var part in parts)
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;

            var key = part.Substring(0, idx).Trim();
            var value = part.Substring(idx + 1).Trim();

            if (key.Length == 0) continue;

            result[key] = value;
            found = true;
        }

        return found;
    }

    private static void TryParseSplitCustomToppingNote(
        string? note,
        out Dictionary<string, string> half1,
        out Dictionary<string, string> half2)
    {
        half1 = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        half2 = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);

        if (string.IsNullOrWhiteSpace(note))
            return;

        var parts = note.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToList();

        foreach (var part in parts)
        {
            if (part.StartsWith("1:", StringComparison.OrdinalIgnoreCase))
            {
                var body = part.Substring(2).Trim();
                if (!TryParseCustomToppingNote(body, out half1))
                    TryParseDisplayToppingNote(body, out half1);
            }
            else if (part.StartsWith("2:", StringComparison.OrdinalIgnoreCase))
            {
                var body = part.Substring(2).Trim();
                if (!TryParseCustomToppingNote(body, out half2))
                    TryParseDisplayToppingNote(body, out half2);
            }
        }
    }

    private void CatalogInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CatalogInput.QuantityText)) return;
        if (sender is not CatalogInput ci) return;

        var raw = (ci.QuantityText ?? "").Trim();
        if (raw.Length == 0)
        {
            RemoveCatalogLine(ci.Navn, ci.Pris);
            RecalcTotal();
            return;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
        {
            ci.QuantityText = "";
            RemoveCatalogLine(ci.Navn, ci.Pris);
            RecalcTotal();
            return;
        }

        UpsertCatalogLine(ci.Navn, ci.Pris, qty);
        RecalcTotal();
    }

    private void UpsertCatalogLine(string navn, decimal pris, int qty)
    {
        var line = Items.FirstOrDefault(i => i.PizzaId == null && i.ItemName == navn && i.UnitPrice == pris);

        if (line == null)
        {
            Items.Add(new OrderItem { PizzaId = null, ItemName = navn, Quantity = qty, UnitPrice = pris, Note = null });
            return;
        }

        var idx = Items.IndexOf(line);
        if (idx >= 0)
        {
            Items[idx] = new OrderItem
            {
                PizzaId = null,
                ItemName = navn,
                Quantity = qty,
                UnitPrice = pris,
                Note = line.Note
            };
        }
    }

    private void RemoveCatalogLine(string navn, decimal pris)
    {
        var line = Items.FirstOrDefault(i => i.PizzaId == null && i.ItemName == navn && i.UnitPrice == pris);
        if (line != null) Items.Remove(line);
    }

    private async Task AutoLoadPizzaAsync(string value, CancellationToken token)
    {
        try { await Task.Delay(120, token); } catch { return; }
        if (token.IsCancellationRequested) return;

        var raw = (value ?? "").Trim().ToUpperInvariant();

        if (TryParseSplit(raw, out var p1, out var p2))
        {
            await LoadSplitPizzaAsync(p1, p2, token);
            return;
        }

        ClearSplitDraft();

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nr))
        {
            ClearActivePizzaDraft();
            return;
        }

        var pizza = await _db.Pizzas
            .Include(p => p.PizzaToppings).ThenInclude(pt => pt.Topping)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IsActive && p.Nummer == nr);

        if (token.IsCancellationRequested) return;

        if (pizza == null)
        {
            ClearActivePizzaDraft();
            return;
        }

        ActivePizza = pizza;
        ActivePizzaTitle = $"{pizza.Nummer} {pizza.Navn}";
        SplitTitle = "";
        ActiveHalfLabel = "";

        UpdateBaseToppingsFromPizza(pizza);

        DisplayIngredients.Clear();
        foreach (var t in pizza.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
            DisplayIngredients.Add(t);

        ApplyCurrentToppingStateToBoxes();
        RefreshActivePizzaPrice();
        RecalcTotal();
    }

    private static bool TryParseSplit(string raw, out int p1, out int p2)
    {
        p1 = 0; p2 = 0;
        if (!raw.StartsWith("D")) return false;
        var s = raw.Substring(1);
        var parts = s.Split('/');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out p1)) return false;
        if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out p2)) return false;
        return p1 > 0 && p2 > 0;
    }

    private async Task LoadSplitPizzaAsync(int p1, int p2, CancellationToken token)
    {
        var pizzas = await _db.Pizzas
            .Include(p => p.PizzaToppings).ThenInclude(pt => pt.Topping)
            .AsNoTracking()
            .Where(p => p.IsActive && (p.Nummer == p1 || p.Nummer == p2))
            .ToListAsync();

        if (token.IsCancellationRequested) return;

        _splitPizza1 = pizzas.FirstOrDefault(x => x.Nummer == p1);
        _splitPizza2 = pizzas.FirstOrDefault(x => x.Nummer == p2);

        if (_splitPizza1 == null || _splitPizza2 == null)
        {
            ClearSplitDraft();
            ClearActivePizzaDraft();
            return;
        }

        IsSplitPizza = true;
        _activeHalf = 1;

        ActivePizza = _splitPizza1;
        ActivePizzaTitle = $"DELT {p1}/{p2}";
        SplitTitle = $"1: {_splitPizza1.Nummer} {_splitPizza1.Navn}   |   2: {_splitPizza2.Nummer} {_splitPizza2.Navn}";

        _half1ToppingValues.Clear();
        _half2ToppingValues.Clear();

        UpdateDisplayIngredientsForActiveHalf();
        UpdateActiveHalfLabel();
        ApplyCurrentToppingStateToBoxes();

        RefreshActivePizzaPrice();
        RefreshSplitPrice(p1, p2);
        RecalcTotal();
    }

    private decimal CalculatePricedToppingExtra(Dictionary<string, string> toppingValues)
    {
        decimal total = 0m;

        foreach (var kv in toppingValues)
        {
            if (!TryGetPricedToppingUnitPrice(kv.Key, out var unitPrice))
                continue;

            var raw = (kv.Value ?? "").Trim();
            if (raw.Length == 0)
                continue;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) && qty > 0)
            {
                total += unitPrice * qty;
                continue;
            }

            if (raw.Equals("+", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("x", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("xx", StringComparison.OrdinalIgnoreCase))
            {
                total += unitPrice;
            }
        }

        return total;
    }

    private void RefreshSplitPrice(int p1, int p2)
    {
        if (_splitPizza1 == null || _splitPizza2 == null)
        {
            ActivePizzaPrice = 0;
            return;
        }

        var s = (SizeText ?? "").Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(s))
            s = "S";

        decimal PriceFor(Pizza p) => s switch
        {
            "S" => p.PrisLarge,
            "G" => p.PrisGlutenfri,
            "M" => p.PrisMedium,
            _ => 0
        };

        var basePrice = Math.Max(PriceFor(_splitPizza1), PriceFor(_splitPizza2));
        var extra = CalculatePricedToppingExtra(_half1ToppingValues) + CalculatePricedToppingExtra(_half2ToppingValues);

        ActivePizzaPrice = basePrice + extra;
    }

    private void RefreshActivePizzaPrice()
    {
        if (IsSplitPizza)
        {
            if (_splitPizza1 != null && _splitPizza2 != null)
                RefreshSplitPrice(_splitPizza1.Nummer, _splitPizza2.Nummer);
            else
                ActivePizzaPrice = 0;
            return;
        }

        if (ActivePizza == null)
        {
            ActivePizzaPrice = 0;
            return;
        }

        var s = (SizeText ?? "").Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(s))
            s = "S";

        var basePrice = s switch
        {
            "S" => ActivePizza.PrisLarge,
            "G" => ActivePizza.PrisGlutenfri,
            "M" => ActivePizza.PrisMedium,
            _ => 0
        };

        ActivePizzaPrice = basePrice + CalculatePricedToppingExtra(_normalToppingValues);
    }

    private void SetActiveHalf(int half)
    {
        if (!IsSplitPizza) return;
        if (half != 1 && half != 2) return;

        _activeHalf = half;
        UpdateActiveHalfLabel();
        UpdateDisplayIngredientsForActiveHalf();
        ApplyCurrentToppingStateToBoxes();
    }

    private void UpdateActiveHalfLabel() => ActiveHalfLabel = _activeHalf == 1 ? "Redigerer halvdel 1" : "Redigerer halvdel 2";

    private void UpdateDisplayIngredientsForActiveHalf()
    {
        DisplayIngredients.Clear();
        var p = _activeHalf == 1 ? _splitPizza1 : _splitPizza2;
        if (p == null) return;

        UpdateBaseToppingsFromPizza(p);

        foreach (var t in p.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
            DisplayIngredients.Add(t);
    }

    private void UpdateBaseToppingsFromPizza(Pizza pizza)
    {
        _currentBaseToppings.Clear();

        foreach (var t in pizza.PizzaToppings.Select(x => x.Topping.Navn))
            _currentBaseToppings.Add(t);
    }

    private void ApplyCurrentToppingStateToBoxes()
    {
        var store = GetCurrentToppingStore();

        _suppressToppingEvents = true;
        foreach (var ti in ToppingInputs)
        {
            if (store.TryGetValue(ti.Navn, out var customValue))
                ti.Input = customValue;
            else if (_currentBaseToppings.Contains(ti.Navn))
                ti.Input = "x";
            else
                ti.Input = "";
        }
        _suppressToppingEvents = false;
    }

    private void ClearSplitDraft()
    {
        IsSplitPizza = false;
        _splitPizza1 = null;
        _splitPizza2 = null;
        SplitTitle = "";
        ActiveHalfLabel = "";
        _half1ToppingValues.Clear();
        _half2ToppingValues.Clear();
        _activeHalf = 1;
    }

    private void ClearActivePizzaDraft()
    {
        ActivePizza = null;
        ActivePizzaTitle = "";
        ActivePizzaPrice = 0;

        DisplayIngredients.Clear();
        _normalToppingValues.Clear();
        _currentBaseToppings.Clear();
        ApplyCurrentToppingStateToBoxes();
    }

    public void ClearEntireOrder()
    {
        Items.Clear();
        SelectedItem = null;

        foreach (var ci in LeftCatalogInputs)
            ci.QuantityText = "";

        foreach (var ci in RightCatalogInputs)
            ci.QuantityText = "";

        _suppressToppingEvents = true;
        foreach (var ti in ToppingInputs)
            ti.Input = "";
        _suppressToppingEvents = false;

        _normalToppingValues.Clear();
        _half1ToppingValues.Clear();
        _half2ToppingValues.Clear();

        Phone = "";
        CustomerName = "";
        AddressText = "";
        OrderNotesText = "";
        IsInfoPopupOpen = false;

        TimeText = DateTime.Now.AddMinutes(30).ToString("HH:mm");

        _suppressPizzaLoad = true;
        PizzaNrText = "";
        _suppressPizzaLoad = false;

        SizeText = "";
        ActivePizzaQuantity = 1;

        ClearSplitDraft();
        ClearActivePizzaDraft();

        AddressSuggestions.Clear();

        DeliveryType = DeliveryType.Delivery;
        DeliveryInputText = "B";
        SyncDriverFee();

        RecalcTotal();
    }

    private void AddOrUpdatePizza()
    {
        if (ActivePizza == null) return;

        RefreshActivePizzaPrice();

        var s = (SizeText ?? "").Trim().ToUpperInvariant();
        var sizeLabel = s is "M" or "S" or "G" ? s : "S";

        string? displayNote;
        string itemName;

        if (IsSplitPizza && _splitPizza1 != null && _splitPizza2 != null)
        {
            var n1 = BuildCustomToppingDisplayNote(_half1ToppingValues);
            var n2 = BuildCustomToppingDisplayNote(_half2ToppingValues);

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(n1)) sb.Append("1: ").Append(n1);
            if (!string.IsNullOrWhiteSpace(n2))
            {
                if (sb.Length > 0) sb.Append(" || ");
                sb.Append("2: ").Append(n2);
            }

            displayNote = sb.Length == 0 ? null : sb.ToString();
            itemName = $"DELT {_splitPizza1.Nummer}/{_splitPizza2.Nummer} ({sizeLabel})";
        }
        else
        {
            var n = BuildCustomToppingDisplayNote(_normalToppingValues);
            displayNote = string.IsNullOrWhiteSpace(n) ? null : n;
            itemName = $"{ActivePizza.Nummer} {ActivePizza.Navn} ({sizeLabel})";
        }

        var newLine = new OrderItem
        {
            PizzaId = ActivePizza.Id,
            ItemName = itemName,
            Quantity = Math.Max(1, ActivePizzaQuantity),
            UnitPrice = ActivePizzaPrice,
            Note = displayNote
        };

        var feeIndex = Items.ToList().FindIndex(i => i.ItemName == "Kj.tillegg");
        if (feeIndex < 0) feeIndex = Items.Count;

        if (IsEditingPizza && _editingPizzaIndex >= 0)
        {
            var insertIndex = Math.Min(_editingPizzaIndex, feeIndex);
            Items.Insert(insertIndex, newLine);

            _editingPizzaIndex = -1;
            _editingOriginalLine = null;
            IsEditingPizza = false;
        }
        else
        {
            Items.Insert(feeIndex, newLine);
        }

        _suppressPizzaLoad = true;
        PizzaNrText = "";
        _suppressPizzaLoad = false;

        SizeText = "";
        ActivePizzaQuantity = 1;

        ClearSplitDraft();
        ClearActivePizzaDraft();
        RecalcTotal();
    }

    private async Task EditSelectedPizzaAsync()
    {
        if (SelectedItem == null) return;
        if (SelectedItem.PizzaId == null) return;

        _editingPizzaIndex = Items.IndexOf(SelectedItem);
        _editingOriginalLine = SelectedItem;

        Items.Remove(SelectedItem);
        SelectedItem = null;
        RecalcTotal();

        var pizzaId = _editingOriginalLine.PizzaId!.Value;
        var pizza = await _db.Pizzas
            .Include(p => p.PizzaToppings).ThenInclude(pt => pt.Topping)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pizzaId);

        if (pizza == null)
        {
            Items.Insert(Math.Min(_editingPizzaIndex, Items.Count), _editingOriginalLine);
            _editingPizzaIndex = -1;
            _editingOriginalLine = null;
            RecalcTotal();
            return;
        }

        IsEditingPizza = true;

        ActivePizza = pizza;
        ActivePizzaTitle = $"{pizza.Nummer} {pizza.Navn}";

        _suppressPizzaLoad = true;
        PizzaNrText = pizza.Nummer.ToString(CultureInfo.InvariantCulture);
        _suppressPizzaLoad = false;

        SizeText = PizzaSizeFromItemName(_editingOriginalLine.ItemName);
        ActivePizzaQuantity = Math.Max(1, _editingOriginalLine.Quantity);

        DisplayIngredients.Clear();
        foreach (var t in pizza.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
            DisplayIngredients.Add(t);

        UpdateBaseToppingsFromPizza(pizza);

        _normalToppingValues.Clear();

        if (TryParseCustomToppingNote(_editingOriginalLine.Note, out var customValues))
        {
            foreach (var kv in customValues)
                _normalToppingValues[kv.Key] = kv.Value;
        }
        else if (TryParseDisplayToppingNote(_editingOriginalLine.Note, out var displayValues))
        {
            foreach (var kv in displayValues)
                _normalToppingValues[kv.Key] = kv.Value;
        }
        else
        {
            ParseNote(_editingOriginalLine.Note, out var uten, out var ekstra);

            foreach (var u in uten)
                _normalToppingValues[u] = "--";

            foreach (var ex in ekstra)
                _normalToppingValues[ex] = "XX";
        }

        ApplyCurrentToppingStateToBoxes();
        RefreshActivePizzaPrice();
        RecalcTotal();
    }

    private void CancelPizzaEdit()
    {
        if (IsEditingPizza && _editingOriginalLine != null && _editingPizzaIndex >= 0)
        {
            var feeIndex = Items.ToList().FindIndex(i => i.ItemName == "Kj.tillegg");
            if (feeIndex < 0) feeIndex = Items.Count;

            var insertIndex = Math.Min(_editingPizzaIndex, feeIndex);
            Items.Insert(insertIndex, _editingOriginalLine);
        }

        _editingPizzaIndex = -1;
        _editingOriginalLine = null;
        IsEditingPizza = false;

        _suppressPizzaLoad = true;
        PizzaNrText = "";
        _suppressPizzaLoad = false;

        SizeText = "";
        ActivePizzaQuantity = 1;

        ClearSplitDraft();
        ClearActivePizzaDraft();
        RecalcTotal();
    }

    private async Task AutoLookupCustomerAsync(string value, CancellationToken token)
    {
        try { await Task.Delay(200, token); } catch { return; }
        if (token.IsCancellationRequested) return;

        var p = NormalizePhone(value);
        if (p.Length < 7) return;

        var cust = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Phone == p);
        if (token.IsCancellationRequested) return;

        if (cust == null) return;

        CustomerName = cust.Name ?? "";
        AddressText = cust.AddressText ?? "";
    }

    private static string NormalizePhone(string s) => new string((s ?? "").Where(char.IsDigit).ToArray());

    public void SelectAddressSuggestion(string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        AddressText = suggestion;
        AddressSuggestions.Clear();
    }

    private void RemoveSelectedItem()
    {
        if (SelectedItem == null) return;
        Items.Remove(SelectedItem);
        SelectedItem = null;
        RecalcTotal();
    }

    private void SyncDriverFee()
    {
        var feeLine = Items.FirstOrDefault(i => i.ItemName == "Kj.tillegg");

        if (DeliveryType == DeliveryType.Delivery)
        {
            if (feeLine == null)
            {
                Items.Add(new OrderItem
                {
                    PizzaId = null,
                    ItemName = "Kj.tillegg",
                    Quantity = 1,
                    UnitPrice = _driverFeePrice,
                    Note = null
                });
            }
            else
            {
                feeLine.UnitPrice = _driverFeePrice;
            }
        }
        else
        {
            if (feeLine != null)
                Items.Remove(feeLine);
        }
    }

    private void RecalcTotal()
    {
        var totalFromCart = Items.Sum(i => i.UnitPrice * i.Quantity);

        if (ActivePizza != null || IsSplitPizza)
        {
            var pizzaQuantity = Math.Max(1, ActivePizzaQuantity);
            Total = totalFromCart + (ActivePizzaPrice * pizzaQuantity);
        }
        else
        {
            Total = totalFromCart;
        }
    }

    private async Task SaveAndPrintAsync()
    {
        // Hvis brukeren har valgt/redigert en pizza, men glemmer å trykke PageDown/Legg til,
        // legges den automatisk inn før utskrift.
        if (ActivePizza != null)
        {
            AddOrUpdatePizza();
        }
        else if (IsEditingPizza)
        {
            CancelPizzaEdit();
        }

        if (Items.Count == 0) return;
        if (Items.All(i => i.ItemName == "Kj.tillegg")) return;

        Customer? customer = null;
        var p = NormalizePhone(Phone);
        if (p.Length >= 7)
        {
            customer = await _db.Customers.FirstOrDefaultAsync(c => c.Phone == p);
            if (customer == null)
            {
                customer = new Customer { Phone = p };
                _db.Customers.Add(customer);
            }

            customer.Name = (CustomerName ?? "").Trim();
            customer.AddressText = (AddressText ?? "").Trim();
            customer.UpdatedAtUtc = DateTime.UtcNow;
            customer.LastOrderAtUtc = DateTime.UtcNow;
            customer.RetentionUntilUtc = customer.LastOrderAtUtc.Value.AddYears(1);
        }

        var scheduledLocal = TryParseTime(TimeText);

        var scheduledNote = scheduledLocal == null
            ? null
            : $"{(DeliveryType == DeliveryType.Delivery ? "BRINGES:" : "HENTES:")} {scheduledLocal:dd.MM.yyyy HH:mm}";

        var userNote = (OrderNotesText ?? "").Trim();
        if (userNote.Length == 0) userNote = null;

        var notes = string.Join(" | ", new[] { scheduledNote, userNote }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (notes.Length == 0) notes = null;

        var order = new Order
        {
            Customer = customer,
            DeliveryType = DeliveryType,
            Status = OrderStatus.New,
            CreatedAtUtc = DateTime.UtcNow,
            Notes = notes,
            Items = Items.Select(i => new OrderItem
            {
                PizzaId = i.PizzaId,
                ItemName = i.ItemName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Note = EncodeLineNoteForPrint(i)
            }).ToList()
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        _printer.PrintOrder(order);

        Items.Clear();

        foreach (var ci in LeftCatalogInputs)
            ci.QuantityText = "";

        foreach (var ci in RightCatalogInputs)
            ci.QuantityText = "";

        _suppressToppingEvents = true;
        foreach (var ti in ToppingInputs)
            ti.Input = "";
        _suppressToppingEvents = false;

        _normalToppingValues.Clear();
        _half1ToppingValues.Clear();
        _half2ToppingValues.Clear();

        Total = 0;
        Phone = "";
        CustomerName = "";
        AddressText = "";
        OrderNotesText = "";
        IsInfoPopupOpen = false;

        DeliveryType = DeliveryType.Delivery;
        DeliveryInputText = "B";
        SyncDriverFee();

        TimeText = DateTime.Now.AddMinutes(30).ToString("HH:mm");

        _suppressPizzaLoad = true;
        PizzaNrText = "";
        _suppressPizzaLoad = false;

        SizeText = "";
        ActivePizzaQuantity = 1;

        ClearSplitDraft();
        ClearActivePizzaDraft();

        RecalcTotal();
    }

    private string? EncodeLineNoteForPrint(OrderItem line)
    {
        if (line.PizzaId == null || string.IsNullOrWhiteSpace(line.Note))
            return line.Note;

        if ((line.ItemName ?? "").StartsWith("DELT ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = (line.Note ?? "")
                .Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToList();

            var sb = new StringBuilder();

            foreach (var part in parts)
            {
                if (part.StartsWith("1:", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseDisplayToppingNote(part.Substring(2).Trim(), out var h1))
                    {
                        if (sb.Length > 0) sb.Append(" || ");
                        sb.Append("1: ").Append(BuildCustomToppingEncodedNote(h1));
                    }
                }
                else if (part.StartsWith("2:", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseDisplayToppingNote(part.Substring(2).Trim(), out var h2))
                    {
                        if (sb.Length > 0) sb.Append(" || ");
                        sb.Append("2: ").Append(BuildCustomToppingEncodedNote(h2));
                    }
                }
            }

            return sb.Length == 0 ? line.Note : sb.ToString();
        }

        if (TryParseDisplayToppingNote(line.Note, out var values))
            return BuildCustomToppingEncodedNote(values);

        return line.Note;
    }

    private DateTime? TryParseTime(string? timeText)
    {
        var s = (timeText ?? "").Trim();
        if (s.Length == 0) return null;

        if (!TimeSpan.TryParseExact(s, @"hh\:mm", CultureInfo.InvariantCulture, out var ts) &&
            !TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out ts))
            return null;

        var dt = DateTime.Today.Add(ts);
        if (dt < DateTime.Now.AddMinutes(-5))
            dt = dt.AddDays(1);

        return dt;
    }

    private static string PizzaSizeFromItemName(string? itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return "";

        var start = itemName.LastIndexOf('(');
        var end = itemName.LastIndexOf(')');
        if (start < 0 || end < 0 || end <= start) return "";

        var s = itemName.Substring(start + 1, end - start - 1).Trim().ToUpperInvariant();
        if (s == "L") return "S";
        return s is "M" or "S" or "G" ? s : "S";
    }

    private static void ParseNote(string? note, out string[] uten, out string[] ekstra)
    {
        uten = Array.Empty<string>();
        ekstra = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(note)) return;

        var parts = note.Split('|').Select(x => x.Trim()).ToList();

        foreach (var p in parts)
        {
            if (p.StartsWith("Uten:", StringComparison.CurrentCultureIgnoreCase))
            {
                var s = p.Substring(5).Trim();
                uten = s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            }
            else if (p.StartsWith("Ekstra:", StringComparison.CurrentCultureIgnoreCase))
            {
                var s = p.Substring(7).Trim();
                ekstra = s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            }
        }
    }
}