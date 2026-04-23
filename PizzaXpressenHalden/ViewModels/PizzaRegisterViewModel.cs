using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PizzaXpressenHalden.Models;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace PizzaXpressenHalden.ViewModels;

public partial class PizzaRegisterViewModel : ObservableObject
{
    private readonly AppDbContext _db;

    public ObservableCollection<Pizza> Pizzas { get; } = new();
    public ObservableCollection<PriceRegistryItem> CatalogItems { get; } = new();

    public ObservableCollection<string> CatalogCategories { get; } = new()
    {
        "Drikke",
        "Tilbehør",
        "Ekstra",
        "Kjøring"
    };

    [ObservableProperty]
    private Pizza? selectedPizza;

    [ObservableProperty]
    private PriceRegistryItem? selectedCatalogItem;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand NewCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }

    public IAsyncRelayCommand RefreshCatalogCommand { get; }
    public IRelayCommand NewCatalogCommand { get; }
    public IAsyncRelayCommand SaveCatalogCommand { get; }
    public IAsyncRelayCommand DeleteCatalogCommand { get; }

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

    private async Task LoadAllAsync()
    {
        await LoadPizzasAsync();
        await LoadCatalogAsync();
    }

    private bool CanDelete() => SelectedPizza is not null;
    private bool CanSave() => SelectedPizza is not null;

    private bool CanDeleteCatalog() => SelectedCatalogItem is not null;
    private bool CanSaveCatalog() => SelectedCatalogItem is not null;

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

        var drinks = await _db.Drinks
            .AsNoTracking()
            .OrderBy(x => x.Navn)
            .ToListAsync();

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

        var tilbehor = await _db.Tilbehor
            .AsNoTracking()
            .OrderBy(x => x.Navn)
            .ToListAsync();

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

        var extras = await _db.Extras
            .AsNoTracking()
            .OrderBy(x => x.Navn)
            .ToListAsync();

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

        if (SelectedPizza.Id == 0)
        {
            var entity = new Pizza
            {
                Nummer = SelectedPizza.Nummer,
                Navn = SelectedPizza.Navn,
                Beskrivelse = SelectedPizza.Beskrivelse,
                PrisMedium = SelectedPizza.PrisMedium,
                PrisLarge = SelectedPizza.PrisLarge,
                PrisGlutenfri = SelectedPizza.PrisGlutenfri,
                IsActive = SelectedPizza.IsActive
            };

            _db.Pizzas.Add(entity);
            await _db.SaveChangesAsync();

            await LoadPizzasAsync();
            SelectedPizza = Pizzas.FirstOrDefault(x => x.Nummer == entity.Nummer);
            return;
        }

        var existing = await _db.Pizzas.FirstAsync(p => p.Id == SelectedPizza.Id);

        existing.Nummer = SelectedPizza.Nummer;
        existing.Navn = SelectedPizza.Navn;
        existing.Beskrivelse = SelectedPizza.Beskrivelse;
        existing.PrisMedium = SelectedPizza.PrisMedium;
        existing.PrisLarge = SelectedPizza.PrisLarge;
        existing.PrisGlutenfri = SelectedPizza.PrisGlutenfri;
        existing.IsActive = SelectedPizza.IsActive;

        await _db.SaveChangesAsync();

        await LoadPizzasAsync();
        SelectedPizza = Pizzas.FirstOrDefault(x => x.Id == existing.Id);
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

        var id = SelectedPizza.Id;
        var entity = await _db.Pizzas.FirstOrDefaultAsync(p => p.Id == id);

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

        var label = SelectedCatalogItem.Category == "Kjøring"
            ? "Kjøretillegg"
            : SelectedCatalogItem.Navn;

        var confirm = MessageBox.Show(
            $"Vil du slette '{label}'?",
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
        SelectedCatalogItem = CatalogItems.FirstOrDefault();
    }

    public partial class PriceRegistryItem : ObservableObject
    {
        [ObservableProperty]
        private int id;

        [ObservableProperty]
        private string category = "";

        [ObservableProperty]
        private string navn = "";

        [ObservableProperty]
        private decimal pris;

        [ObservableProperty]
        private bool isActive = true;

        public string DisplayText =>
            $"{Category} - {Navn} ({Pris:0.00} kr)";
    }
}