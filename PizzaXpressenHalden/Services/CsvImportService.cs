using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PizzaXpressenHalden.Models;

namespace PizzaXpressenHalden;

public class CsvImportService
{
    private readonly AppDbContext _db;

    public CsvImportService(AppDbContext db) => _db = db;

    private record PizzaCsv(int nummer, string navn, decimal pris_medium, decimal pris_large, decimal pris_glutenfri, string? beskrivelse);
    private record ToppingCsv(string navn, string kjøkken_navn, int sortering, int justerbar);
    private record PizzaToppingRelCsv(int pizza_nummer, string topping_navn);
    private record NamePriceCsv(string navn, decimal pris);

    public async Task ImportMenuIfEmptyAsync(string pizzaerPath, string toppingPath, string relPath, string drikkerPath, string extrasPath, string tilbehorPath)
    {
        if (await _db.Pizzas.AnyAsync() || await _db.Toppings.AnyAsync()) return;

        await using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var p in ReadCsv<PizzaCsv>(pizzaerPath))
        {
            _db.Pizzas.Add(new Pizza
            {
                Nummer = p.nummer,
                Navn = p.navn,
                Beskrivelse = p.beskrivelse,
                PrisMedium = p.pris_medium,
                PrisLarge = p.pris_large,
                PrisGlutenfri = p.pris_glutenfri,
                IsActive = true
            });
        }
        await _db.SaveChangesAsync();

        foreach (var t in ReadCsv<ToppingCsv>(toppingPath))
        {
            _db.Toppings.Add(new Topping
            {
                Navn = t.navn,
                KjokkenNavn = t.kjøkken_navn,
                Sortering = t.sortering,
                Justerbar = t.justerbar == 1
            });
        }
        await _db.SaveChangesAsync();

        var pizzaByNummer = await _db.Pizzas.ToDictionaryAsync(x => x.Nummer, x => x.Id);
        var toppingByNavn = await _db.Toppings.ToDictionaryAsync(x => x.Navn, x => x.Id);

        foreach (var r in ReadCsv<PizzaToppingRelCsv>(relPath))
        {
            if (!pizzaByNummer.TryGetValue(r.pizza_nummer, out var pizzaId)) continue;
            if (!toppingByNavn.TryGetValue(r.topping_navn, out var toppingId)) continue;
            _db.PizzaToppings.Add(new PizzaTopping { PizzaId = pizzaId, ToppingId = toppingId });
        }
        await _db.SaveChangesAsync();

        foreach (var d in ReadCsv<NamePriceCsv>(drikkerPath))
            _db.Drinks.Add(new Drink { Navn = d.navn, Pris = d.pris, IsActive = true });

        foreach (var e in ReadCsv<NamePriceCsv>(extrasPath))
            _db.Extras.Add(new Extra { Navn = e.navn, Pris = e.pris, IsActive = true });

        foreach (var t in ReadCsv<NamePriceCsv>(tilbehorPath))
            _db.Tilbehor.Add(new Tilbehor { Navn = t.navn, Pris = t.pris, IsActive = true });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static List<T> ReadCsv<T>(string path)
    {
        var cfg = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            PrepareHeaderForMatch = args => args.Header?.Trim().ToLowerInvariant()
        };

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, cfg);

        return csv.GetRecords<T>().ToList();
    }
}
