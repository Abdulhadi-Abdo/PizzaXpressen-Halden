using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PizzaXpressenHalden.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace PizzaXpressenHalden.ViewModels;

public partial class PizzaRegisterViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    public ObservableCollection<Pizza> Pizzas { get; } = new();
    public ObservableCollection<PriceRegistryItem> CatalogItems { get; } = new();
    public ObservableCollection<Topping> Toppings { get; } = new();

    public ObservableCollection<string> CatalogCategories { get; } = new()
    {
        "Drikke",
        "Tilbehør",
        "Ekstra",
        "Kjøring"
    };

    [ObservableProperty] private Pizza? selectedPizza;
    [ObservableProperty] private PriceRegistryItem? selectedCatalogItem;
    [ObservableProperty] private Topping? selectedTopping;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand NewCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }

    public IAsyncRelayCommand RefreshCatalogCommand { get; }
    public IRelayCommand NewCatalogCommand { get; }
    public IAsyncRelayCommand SaveCatalogCommand { get; }
    public IAsyncRelayCommand DeleteCatalogCommand { get; }

    public IAsyncRelayCommand RefreshToppingsCommand { get; }
    public IRelayCommand NewToppingCommand { get; }
    public IAsyncRelayCommand SaveToppingCommand { get; }
    public IAsyncRelayCommand DeleteToppingCommand { get; }

    public PizzaRegisterViewModel(AppDbContext db)
    {
        _db = db;

        RefreshCommand = new AsyncRelayCommand(LoadPizzasAsync);
        NewCommand = new RelayCommand(NewPizza);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, CanDelete);

        RefreshCatalogCommand = new AsyncRelayCommand(LoadCatalogAsync);
        NewCatalogCommand = new RelayCommand(NewCatalogItem);
        SaveCatalogCommand = new AsyncRelayCommand(SaveCatalogAsync, CanSaveCatalog);
        DeleteCatalogCommand = new AsyncRelayCommand(DeleteCatalogAsync, CanDeleteCatalog);

        RefreshToppingsCommand = new AsyncRelayCommand(LoadToppingsAsync);
        NewToppingCommand = new RelayCommand(NewTopping);
        SaveToppingCommand = new AsyncRelayCommand(SaveToppingAsync, CanSaveTopping);
        DeleteToppingCommand = new AsyncRelayCommand(DeleteToppingAsync, CanDeleteTopping);

        _ = LoadAllAsync();
    }

    partial void OnSelectedPizzaChanged(Pizza? value)
    {
        DeleteCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCatalogItemChanged(PriceRegistryItem? value)
    {
        DeleteCatalogCommand.NotifyCanExecuteChanged();
        SaveCatalogCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedToppingChanged(Topping? value)
    {
        DeleteToppingCommand.NotifyCanExecuteChanged();
        SaveToppingCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadAllAsync()
    {
        await LoadPizzasAsync();
        await LoadCatalogAsync();
        await LoadToppingsAsync();
    }

    private bool CanDelete() => SelectedPizza is not null;
    private bool CanSave() => SelectedPizza is not null;

    private bool CanDeleteCatalog() => SelectedCatalogItem is not null;
    private bool CanSaveCatalog() => SelectedCatalogItem is not null;

    private bool CanDeleteTopping() => SelectedTopping is not null;
    private bool CanSaveTopping() => SelectedTopping is not null;

    private async Task LoadPizzasAsync()
    {
        Pizzas.Clear();

        var list = await _db.Pizzas
            .AsNoTracking()
            .OrderBy(p => p.Nummer)
            .ToListAsync();

        foreach (var p in list)
            Pizzas.Add(p);

        SelectedPizza = Pizzas.FirstOrDefault();
    }

    private async Task LoadCatalogAsync()
    {
        CatalogItems.Clear();

        var drinks = await _db.Drinks.AsNoTracking().OrderBy(x => x.Navn).ToListAsync();
        foreach (var d in drinks)
        {
            CatalogItems.Add(new PriceRegistryItem
            {
                Id = d.Id,
                Category = "Drikke",
                Navn = d.Navn,
                Pris = d.Pris,
                IsActive = d.IsActive
            });
        }

        var tilbehor = await _db.Tilbehor.AsNoTracking().OrderBy(x => x.Navn).ToListAsync();
        foreach (var t in tilbehor)
        {
            CatalogItems.Add(new PriceRegistryItem
            {
                Id = t.Id,
                Category = "Tilbehør",
                Navn = t.Navn,
                Pris = t.Pris,
                IsActive = t.IsActive
            });
        }

        var extras = await _db.Extras.AsNoTracking().OrderBy(x => x.Navn).ToListAsync();
        foreach (var e in extras)
        {
            CatalogItems.Add(new PriceRegistryItem
            {
                Id = e.Id,
                Category = e.Navn == "Kj.tillegg" ? "Kjøring" : "Ekstra",
                Navn = e.Navn == "Kj.tillegg" ? "Kjøretillegg" : e.Navn,
                Pris = e.Pris,
                IsActive = e.IsActive
            });
        }

        SelectedCatalogItem = CatalogItems.FirstOrDefault();
    }

    private async Task LoadToppingsAsync()
    {
        await NormalizeToppingOrderAsync();

        Toppings.Clear();

        var list = await _db.Toppings
            .AsNoTracking()
            .OrderBy(x => x.Sortering)
            .ThenBy(x => x.Navn)
            .ToListAsync();

        foreach (var t in list)
            Toppings.Add(t);

        SelectedTopping = Toppings.FirstOrDefault();
    }

    private async Task NormalizeToppingOrderAsync()
    {
        var list = await _db.Toppings
            .OrderBy(x => x.Sortering)
            .ThenBy(x => x.Navn)
            .ToListAsync();

        if (list.Count == 0)
            return;

        bool changed = false;
        int expected = 1;

        foreach (var topping in list)
        {
            if (topping.Sortering != expected)
            {
                topping.Sortering = expected;
                changed = true;
            }

            expected++;
        }

        if (changed)
            await _db.SaveChangesAsync();
    }

    private void NewPizza()
    {
        var nextNr = (Pizzas.Count == 0) ? 1 : Pizzas.Max(p => p.Nummer) + 1;

        var p = new Pizza
        {
            Id = 0,
            Nummer = nextNr,
            Navn = "",
            Beskrivelse = "",
            PrisMedium = 0,
            PrisLarge = 0,
            PrisGlutenfri = 0,
            IsActive = true
        };

        Pizzas.Add(p);
        SelectedPizza = p;
    }

    private async Task SaveAsync()
    {
        if (SelectedPizza is null) return;

        var navn = (SelectedPizza.Navn ?? "").Trim();
        var beskrivelse = (SelectedPizza.Beskrivelse ?? "").Trim();

        if (navn.Length == 0)
        {
            MessageBox.Show("Navn må fylles ut.");
            return;
        }

        if (await _db.Pizzas.AnyAsync(x => x.Nummer == SelectedPizza.Nummer && x.Id != SelectedPizza.Id))
        {
            MessageBox.Show($"Pizzanummer {SelectedPizza.Nummer} finnes allerede.");
            return;
        }

        Pizza entity;

        if (SelectedPizza.Id == 0)
        {
            entity = new Pizza
            {
                Nummer = SelectedPizza.Nummer,
                Navn = navn,
                Beskrivelse = beskrivelse,
                PrisMedium = SelectedPizza.PrisMedium,
                PrisLarge = SelectedPizza.PrisLarge,
                PrisGlutenfri = SelectedPizza.PrisGlutenfri,
                IsActive = SelectedPizza.IsActive
            };

            _db.Pizzas.Add(entity);
            await _db.SaveChangesAsync();
        }
        else
        {
            entity = await _db.Pizzas
                .Include(p => p.PizzaToppings)
                .FirstAsync(p => p.Id == SelectedPizza.Id);

            entity.Nummer = SelectedPizza.Nummer;
            entity.Navn = navn;
            entity.Beskrivelse = beskrivelse;
            entity.PrisMedium = SelectedPizza.PrisMedium;
            entity.PrisLarge = SelectedPizza.PrisLarge;
            entity.PrisGlutenfri = SelectedPizza.PrisGlutenfri;
            entity.IsActive = SelectedPizza.IsActive;

            await _db.SaveChangesAsync();
        }

        await SyncPizzaToppingsFromDescriptionAsync(entity);

        await LoadPizzasAsync();
        SelectedPizza = Pizzas.FirstOrDefault(x => x.Id == entity.Id);
    }

    private async Task SyncPizzaToppingsFromDescriptionAsync(Pizza pizza)
    {
        var description = (pizza.Beskrivelse ?? "").Trim();

        var existingPizza = await _db.Pizzas
            .Include(p => p.PizzaToppings)
            .FirstAsync(p => p.Id == pizza.Id);

        var allToppings = await _db.Toppings
            .AsNoTracking()
            .OrderBy(x => x.Sortering)
            .ThenBy(x => x.Navn)
            .ToListAsync();

        var matchedToppingIds = MatchToppingsFromDescription(description, allToppings);

        var currentLinks = await _db.Set<PizzaTopping>()
            .Where(x => x.PizzaId == pizza.Id)
            .ToListAsync();

        _db.Set<PizzaTopping>().RemoveRange(currentLinks);

        foreach (var toppingId in matchedToppingIds)
        {
            _db.Set<PizzaTopping>().Add(new PizzaTopping
            {
                PizzaId = pizza.Id,
                ToppingId = toppingId
            });
        }

        await _db.SaveChangesAsync();
    }

    private static HashSet<int> MatchToppingsFromDescription(string description, List<Topping> allToppings)
    {
        var result = new HashSet<int>();

        if (string.IsNullOrWhiteSpace(description))
            return result;

        var normalizedDescription = NormalizeText(description);

        foreach (var topping in allToppings)
        {
            var toppingName = NormalizeText(topping.Navn);
            if (toppingName.Length == 0)
                continue;

            if (ContainsWholeTerm(normalizedDescription, toppingName))
                result.Add(topping.Id);
        }

        return result;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var s = text.ToLowerInvariant();

        s = s.Replace("&", ",");
        s = s.Replace("/", ",");
        s = s.Replace(" og ", ",");
        s = s.Replace(" m/ ", ",");
        s = s.Replace("med", ",");
        s = s.Replace(".", " ");
        s = s.Replace("  ", " ");

        return s.Trim();
    }

    private static bool ContainsWholeTerm(string text, string term)
    {
        if (text.Length == 0 || term.Length == 0)
            return false;

        var escaped = Regex.Escape(term).Replace("\\ ", @"\s+");
        var pattern = $@"(?<!\p{{L}}){escaped}(?!\p{{L}})";

        return System.Text.RegularExpressions.Regex.IsMatch(
            text,
            pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private async Task DeleteAsync()
    {
        if (SelectedPizza is null) return;

        var confirm = MessageBox.Show(
            $"Vil du slette pizza '{SelectedPizza.Navn}' (#{SelectedPizza.Nummer})?",
            "Bekreft sletting",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        if (SelectedPizza.Id == 0)
        {
            var draft = SelectedPizza;
            Pizzas.Remove(draft);
            SelectedPizza = Pizzas.FirstOrDefault();
            return;
        }

        var links = await _db.Set<PizzaTopping>()
            .Where(x => x.PizzaId == SelectedPizza.Id)
            .ToListAsync();

        if (links.Count > 0)
            _db.Set<PizzaTopping>().RemoveRange(links);

        var entity = await _db.Pizzas.FirstOrDefaultAsync(p => p.Id == SelectedPizza.Id);
        if (entity is null)
        {
            await LoadPizzasAsync();
            return;
        }

        _db.Pizzas.Remove(entity);
        await _db.SaveChangesAsync();

        await LoadPizzasAsync();
        SelectedPizza = Pizzas.FirstOrDefault();
    }

    private void NewCatalogItem()
    {
        var item = new PriceRegistryItem
        {
            Id = 0,
            Category = "Drikke",
            Navn = "",
            Pris = 0,
            IsActive = true
        };

        CatalogItems.Add(item);
        SelectedCatalogItem = item;
    }

    private async Task SaveCatalogAsync()
    {
        if (SelectedCatalogItem is null) return;

        var category = (SelectedCatalogItem.Category ?? "").Trim();
        var navn = (SelectedCatalogItem.Navn ?? "").Trim();

        if (category.Length == 0)
        {
            MessageBox.Show("Velg kategori.");
            return;
        }

        if (category != "Kjøring" && navn.Length == 0)
        {
            MessageBox.Show("Navn må fylles ut.");
            return;
        }

        if (category == "Kjøring")
            navn = "Kj.tillegg";

        if (SelectedCatalogItem.Id == 0)
        {
            switch (category)
            {
                case "Drikke":
                    _db.Drinks.Add(new Drink
                    {
                        Navn = navn,
                        Pris = SelectedCatalogItem.Pris,
                        IsActive = SelectedCatalogItem.IsActive
                    });
                    break;

                case "Tilbehør":
                    _db.Tilbehor.Add(new Tilbehor
                    {
                        Navn = navn,
                        Pris = SelectedCatalogItem.Pris,
                        IsActive = SelectedCatalogItem.IsActive
                    });
                    break;

                case "Ekstra":
                case "Kjøring":
                    _db.Extras.Add(new Extra
                    {
                        Navn = navn,
                        Pris = SelectedCatalogItem.Pris,
                        IsActive = SelectedCatalogItem.IsActive
                    });
                    break;
            }

            await _db.SaveChangesAsync();
            await LoadCatalogAsync();
            return;
        }

        switch (category)
        {
            case "Drikke":
                {
                    var entity = await _db.Drinks.FirstOrDefaultAsync(x => x.Id == SelectedCatalogItem.Id);
                    if (entity != null)
                    {
                        entity.Navn = navn;
                        entity.Pris = SelectedCatalogItem.Pris;
                        entity.IsActive = SelectedCatalogItem.IsActive;
                    }
                    break;
                }

            case "Tilbehør":
                {
                    var entity = await _db.Tilbehor.FirstOrDefaultAsync(x => x.Id == SelectedCatalogItem.Id);
                    if (entity != null)
                    {
                        entity.Navn = navn;
                        entity.Pris = SelectedCatalogItem.Pris;
                        entity.IsActive = SelectedCatalogItem.IsActive;
                    }
                    break;
                }

            case "Ekstra":
            case "Kjøring":
                {
                    var entity = await _db.Extras.FirstOrDefaultAsync(x => x.Id == SelectedCatalogItem.Id);
                    if (entity != null)
                    {
                        entity.Navn = navn;
                        entity.Pris = SelectedCatalogItem.Pris;
                        entity.IsActive = SelectedCatalogItem.IsActive;
                    }
                    break;
                }
        }

        await _db.SaveChangesAsync();
        await LoadCatalogAsync();
    }

    private async Task DeleteCatalogAsync()
    {
        if (SelectedCatalogItem is null) return;

        var confirm = MessageBox.Show(
            $"Vil du slette varen '{SelectedCatalogItem.Navn}'?",
            "Bekreft sletting",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        if (SelectedCatalogItem.Id == 0)
        {
            CatalogItems.Remove(SelectedCatalogItem);
            SelectedCatalogItem = CatalogItems.FirstOrDefault();
            return;
        }

        switch (SelectedCatalogItem.Category)
        {
            case "Drikke":
                {
                    var entity = await _db.Drinks.FirstOrDefaultAsync(x => x.Id == SelectedCatalogItem.Id);
                    if (entity != null) _db.Drinks.Remove(entity);
                    break;
                }

            case "Tilbehør":
                {
                    var entity = await _db.Tilbehor.FirstOrDefaultAsync(x => x.Id == SelectedCatalogItem.Id);
                    if (entity != null) _db.Tilbehor.Remove(entity);
                    break;
                }

            case "Ekstra":
            case "Kjøring":
                {
                    var entity = await _db.Extras.FirstOrDefaultAsync(x => x.Id == SelectedCatalogItem.Id);
                    if (entity != null) _db.Extras.Remove(entity);
                    break;
                }
        }

        await _db.SaveChangesAsync();
        await LoadCatalogAsync();
    }

    private void NewTopping()
    {
        int nextSort = (Toppings.Count == 0) ? 1 : Toppings.Max(x => x.Sortering) + 1;

        var topping = new Topping
        {
            Id = 0,
            Navn = "",
            KjokkenNavn = "",
            Sortering = nextSort,
            Justerbar = true
        };

        Toppings.Add(topping);
        SelectedTopping = topping;
    }

    private async Task SaveToppingAsync()
    {
        if (SelectedTopping is null) return;

        var navn = (SelectedTopping.Navn ?? "").Trim();
        var kjokkenNavn = (SelectedTopping.KjokkenNavn ?? "").Trim();

        if (navn.Length == 0)
        {
            MessageBox.Show("Navn må fylles ut.");
            return;
        }

        if (kjokkenNavn.Length == 0)
            kjokkenNavn = navn;

        if (SelectedTopping.Id == 0)
        {
            _db.Toppings.Add(new Topping
            {
                Navn = navn,
                KjokkenNavn = kjokkenNavn,
                Sortering = SelectedTopping.Sortering,
                Justerbar = SelectedTopping.Justerbar
            });

            await _db.SaveChangesAsync();
            await LoadToppingsAsync();
            return;
        }

        var entity = await _db.Toppings.FirstOrDefaultAsync(x => x.Id == SelectedTopping.Id);
        if (entity == null)
        {
            await LoadToppingsAsync();
            return;
        }

        entity.Navn = navn;
        entity.KjokkenNavn = kjokkenNavn;
        entity.Sortering = SelectedTopping.Sortering;
        entity.Justerbar = SelectedTopping.Justerbar;

        await _db.SaveChangesAsync();
        await LoadToppingsAsync();
        SelectedTopping = Toppings.FirstOrDefault(x => x.Id == entity.Id);
    }

    private async Task DeleteToppingAsync()
    {
        if (SelectedTopping is null) return;

        var confirm = MessageBox.Show(
            $"Vil du slette topping '{SelectedTopping.Navn}'?",
            "Bekreft sletting",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        if (SelectedTopping.Id == 0)
        {
            Toppings.Remove(SelectedTopping);
            SelectedTopping = Toppings.FirstOrDefault();
            return;
        }

        var links = await _db.Set<PizzaTopping>()
            .Where(x => x.ToppingId == SelectedTopping.Id)
            .ToListAsync();

        if (links.Count > 0)
            _db.Set<PizzaTopping>().RemoveRange(links);

        var entity = await _db.Toppings.FirstOrDefaultAsync(x => x.Id == SelectedTopping.Id);
        if (entity != null)
            _db.Toppings.Remove(entity);

        await _db.SaveChangesAsync();
        await LoadToppingsAsync();
    }

    public partial class PriceRegistryItem : ObservableObject
    {
        [ObservableProperty] private int id;
        [ObservableProperty] private string category = "";
        [ObservableProperty] private string navn = "";
        [ObservableProperty] private decimal pris;
        [ObservableProperty] private bool isActive = true;
    }
}