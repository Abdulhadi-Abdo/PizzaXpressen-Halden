// File: ViewModels/PizzaRegisterViewModel.cs
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

    [ObservableProperty]
    private Pizza? selectedPizza;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand NewCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }

    public PizzaRegisterViewModel(AppDbContext db)
    {
        _db = db;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        NewCommand = new RelayCommand(NewPizza);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, CanDelete);

        _ = LoadAsync();
    }

    partial void OnSelectedPizzaChanged(Pizza? value)
    {
        DeleteCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanDelete() => SelectedPizza is not null;

    private bool CanSave() => SelectedPizza is not null;

    private async Task LoadAsync()
    {
        Pizzas.Clear();

        var list = await _db.Pizzas
            .AsNoTracking()
            .OrderBy(p => p.Nummer)
            .ToListAsync();

        foreach (var p in list) Pizzas.Add(p);

        SelectedPizza = Pizzas.FirstOrDefault();
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

            await LoadAsync();
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

        await LoadAsync();
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
            await LoadAsync();
            return;
        }

        _db.Pizzas.Remove(entity); // hard delete
        await _db.SaveChangesAsync();

        await LoadAsync();
        SelectedPizza = Pizzas.FirstOrDefault();
    }
}