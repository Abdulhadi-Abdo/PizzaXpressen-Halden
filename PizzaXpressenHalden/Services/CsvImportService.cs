using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PizzaXpressenHalden.Models;

namespace PizzaXpressenHalden;

public class CsvImportService
{
    private readonly AppDbContext _db;

    public CsvImportService(AppDbContext db) => _db = db;

    public async Task ImportMenuIfEmptyAsync(
        string pizzaerPath,
        string toppingPath,
        string relPath,
        string drikkerPath,
        string extrasPath,
        string tilbehorPath)
    {
        // Importen skal bare kjøres første gang databasen er tom.
        // Hvis du endrer CSV-filene senere, må databasen slettes først
        // eller importlogikken endres.
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
                Navn = t.Navn,
                KjokkenNavn = t.KjokkenNavn,
                Sortering = t.Sortering,
                Justerbar = t.Justerbar
            });
        }

        await _db.SaveChangesAsync();

        var pizzaByNumber = await _db.Pizzas.ToDictionaryAsync(x => x.Nummer, x => x.Id);
        var toppingByName = await _db.Toppings.ToDictionaryAsync(x => x.Navn, x => x.Id);

        foreach (var r in ReadCsv<PizzaToppingRelCsv>(relPath))
        {
            if (!pizzaByNumber.TryGetValue(r.pizza_nummer, out var pizzaId)) continue;
            if (!toppingByName.TryGetValue(r.topping_navn, out var toppingId)) continue;

            _db.PizzaToppings.Add(new PizzaTopping
            {
                PizzaId = pizzaId,
                ToppingId = toppingId
            });
        }

        await _db.SaveChangesAsync();

        foreach (var d in ReadCsv<NamePriceCsv>(drikkerPath))
        {
            _db.Drinks.Add(new Drink
            {
                Navn = d.navn,
                Pris = d.pris,
                IsActive = true
            });
        }

        foreach (var e in ReadCsv<NamePriceCsv>(extrasPath))
        {
            _db.Extras.Add(new Extra
            {
                Navn = e.navn,
                Pris = e.pris,
                IsActive = e.is_active != 0
            });
        }

        foreach (var t in ReadCsv<NamePriceCsv>(tilbehorPath))
        {
            _db.Tilbehor.Add(new Tilbehor
            {
                Navn = t.navn,
                Pris = t.pris,
                IsActive = true
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static List<T> ReadCsv<T>(string path)
    {
        var cfg = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            PrepareHeaderForMatch = args => NormalizeHeader(args.Header)
        };

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, cfg);

        return csv.GetRecords<T>().ToList();
    }

    private static string NormalizeHeader(string? header)
    {
        return (header ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace("ø", "o")
            .Replace("æ", "ae")
            .Replace("å", "a")
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty);
    }

    private sealed class PizzaCsv
    {
        public int nummer { get; set; }
        public string navn { get; set; } = string.Empty;
        public decimal pris_medium { get; set; }
        public decimal pris_large { get; set; }
        public decimal pris_glutenfri { get; set; }
        public string? beskrivelse { get; set; }
    }

    private sealed class ToppingCsv
    {
        public string Navn { get; set; } = string.Empty;
        public string KjokkenNavn { get; set; } = string.Empty;
        public int Sortering { get; set; }
        public bool Justerbar { get; set; }
    }

    private sealed class PizzaToppingRelCsv
    {
        public int pizza_nummer { get; set; }
        public string topping_navn { get; set; } = string.Empty;
    }

    private sealed class NamePriceCsv
    {
        public string navn { get; set; } = string.Empty;
        public decimal pris { get; set; }

        // Brukes av pizza_extras.csv. For drikker og tilbehør finnes ikke feltet,
        // derfor står standardverdien til 1.
        public int is_active { get; set; } = 1;
    }
}