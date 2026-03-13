using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PizzaXpressenHalden.Services;

public class AddressSuggestionService
{
    private readonly List<string> _addresses = new();

    public AddressSuggestionService()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(baseDir, "Data", "halden_adresser.txt");
            if (!File.Exists(path)) return;

            foreach (var line in File.ReadLines(path))
            {
                var s = (line ?? "").Trim();
                if (s.Length > 0) _addresses.Add(s);
            }
        }
        catch
        {
        }
    }

    public IReadOnlyList<string> Suggest(string query, int max = 8)
    {
        var q = (query ?? "").Trim();
        if (q.Length < 2) return Array.Empty<string>();

        var qLower = q.ToLowerInvariant();

        var starts = _addresses
            .Where(a => a.Length > 0 && a.StartsWith(q, StringComparison.CurrentCultureIgnoreCase))
            .Take(max)
            .ToList();

        if (starts.Count >= max) return starts;

        var rest = _addresses
            .Where(a => a.Length > 0 && !a.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) &&
                        a.ToLowerInvariant().Contains(qLower))
            .Take(max - starts.Count);

        starts.AddRange(rest);
        return starts;
    }
}
