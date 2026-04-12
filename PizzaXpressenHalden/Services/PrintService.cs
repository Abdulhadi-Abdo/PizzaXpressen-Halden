using Microsoft.EntityFrameworkCore;
using PizzaXpressenHalden.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using System.IO;

namespace PizzaXpressenHalden;

public class PrintService
{
    private readonly AppDbContext _db;

    public PrintService(AppDbContext db)
    {
        _db = db;
    }

    public void PrintOrder(Order order)
    {
        var full = _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .FirstOrDefault(o => o.Id == order.Id) ?? order;

        PrintDocument(BuildCombinedDocument(full), "PizzaXpressen Halden");
    }

    private static void PrintDocument(FlowDocument doc, string jobName)
    {
        var queue = LocalPrintServer.GetDefaultPrintQueue();

        var width = MmToPx(105);
        var height = MmToPx(297);

        var ticket = queue.DefaultPrintTicket;
        ticket.PageMediaSize = new PageMediaSize(width, height);

        try
        {
            var writer = PrintQueue.CreateXpsDocumentWriter(queue);
            writer.Write(((IDocumentPaginatorSource)doc).DocumentPaginator, ticket);
        }
        catch
        {
            var dlg = new PrintDialog();
            dlg.PrintQueue = queue;
            dlg.PrintTicket = ticket;

            if (dlg.ShowDialog() == true)
                dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, jobName);
        }
    }

    private FlowDocument BuildCombinedDocument(Order order)
    {
        var d = new FlowDocument();
        d.PagePadding = new Thickness(12);
        d.FontFamily = new FontFamily("Consolas");
        d.FontSize = 11;
        d.ColumnWidth = double.PositiveInfinity;

        d.PageWidth = MmToPx(105);
        d.PageHeight = MmToPx(297);

        d.Blocks.Add(BuildKitchenBlock(order));
        d.Blocks.Add(SeparatorBlock());
        d.Blocks.Add(BuildReceiptBlock(order));

        if (order.DeliveryType == DeliveryType.Delivery)
        {
            d.Blocks.Add(SeparatorBlock());
            d.Blocks.Add(BuildDriverBlock(order));
        }

        return d;
    }

    private static Block SeparatorBlock()
    {
        var p = new Paragraph(new Run(" "));
        p.Margin = new Thickness(0, 8, 0, 8);
        p.BorderBrush = Brushes.Black;
        p.BorderThickness = new Thickness(0, 1, 0, 0);
        return p;
    }

    private Block BuildKitchenBlock(Order order)
    {
        var scheduled = GetScheduledLocal(order);
        var type = order.DeliveryType == DeliveryType.Delivery ? "BRINGES" : "HENTES";
        var createdLocal = order.CreatedAtUtc.ToLocalTime();

        var lines = new List<string>
        {
            "KJØKKENLAPP",
            $"Inn kl: {createdLocal:HH:mm}",
            scheduled != null ? $"{type}: {scheduled:HH:mm} {scheduled:dd.MM.yy}" : $"{type}: -",
            ""
        };

        var userNotes = GetUserNotes(order);
        if (!string.IsNullOrWhiteSpace(userNotes))
        {
            lines.Add("MERKNADER:");
            lines.AddRange(WrapMono(userNotes, 34).Select(x => "  " + x));
            lines.Add("");
        }

        bool IsPizzaLine(OrderItem it)
            => it.PizzaId != null || (it.ItemName ?? "").StartsWith("DELT ", StringComparison.OrdinalIgnoreCase);

        foreach (var it in order.Items.Where(IsPizzaLine))
        {
            lines.Add($"{it.Quantity} {it.ItemName}");

            var toppingLines = BuildKitchenToppingTable(it);
            if (toppingLines.Count > 0)
            {
                foreach (var line in toppingLines)
                    lines.Add(line);
            }

            lines.Add("");
        }

        foreach (var it in order.Items.Where(x => !IsPizzaLine(x) && x.PizzaId == null && x.ItemName != "Kj.tillegg"))
        {
            lines.Add($"{it.Quantity} {it.ItemName}");
            if (!string.IsNullOrWhiteSpace(it.Note))
                lines.Add($"  {it.Note}");
        }

        return MonoBlock(lines, true);
    }

    private List<string> BuildKitchenToppingTable(OrderItem item)
    {
        var result = new List<string>();

        if ((item.ItemName ?? "").StartsWith("DELT ", StringComparison.OrdinalIgnoreCase))
        {
            result.AddRange(BuildSplitPizzaKitchenTable(item));
            return result;
        }

        var baseToppings = new List<string>();

        if (item.PizzaId.HasValue)
        {
            baseToppings = _db.Pizzas
                .AsNoTracking()
                .Where(p => p.Id == item.PizzaId.Value)
                .Include(p => p.PizzaToppings)
                .ThenInclude(pt => pt.Topping)
                .SelectMany(p => p.PizzaToppings)
                .OrderBy(pt => pt.Topping.Sortering)
                .Select(pt => pt.Topping.Navn)
                .ToList();
        }

        ParseStandardNote(item.Note, out var removes, out var adds);

        var rows = new List<(string Mark, string Name)>();

        foreach (var t in baseToppings)
        {
            if (removes.Contains(t, StringComparer.CurrentCultureIgnoreCase))
                rows.Add(("-", t));
            else
                rows.Add(("x", t));
        }

        foreach (var t in adds)
        {
            rows.Add(("+", t));
        }

        result.AddRange(FormatMarkedToppings(rows));
        return result;
    }

    private List<string> BuildSplitPizzaKitchenTable(OrderItem item)
    {
        var result = new List<string>();

        var name = item.ItemName ?? "";
        var m = Regex.Match(name, @"DELT\s+(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            if (!string.IsNullOrWhiteSpace(item.Note))
                result.Add("  " + item.Note);
            return result;
        }

        var nr1 = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var nr2 = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

        var pizza1 = _db.Pizzas
            .AsNoTracking()
            .Include(p => p.PizzaToppings)
            .ThenInclude(pt => pt.Topping)
            .FirstOrDefault(p => p.Nummer == nr1);

        var pizza2 = _db.Pizzas
            .AsNoTracking()
            .Include(p => p.PizzaToppings)
            .ThenInclude(pt => pt.Topping)
            .FirstOrDefault(p => p.Nummer == nr2);

        ParseSplitNote(item.Note, out var half1Removes, out var half1Adds, out var half2Removes, out var half2Adds);

        if (pizza1 != null)
        {
            result.Add($"  HALVDEL 1 - {pizza1.Nummer} {pizza1.Navn}");
            var rows1 = new List<(string Mark, string Name)>();

            foreach (var t in pizza1.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
            {
                if (half1Removes.Contains(t, StringComparer.CurrentCultureIgnoreCase))
                    rows1.Add(("-", t));
                else
                    rows1.Add(("x", t));
            }

            foreach (var t in half1Adds)
                rows1.Add(("+", t));

            result.AddRange(FormatMarkedToppings(rows1, "    "));
        }

        if (pizza2 != null)
        {
            result.Add($"  HALVDEL 2 - {pizza2.Nummer} {pizza2.Navn}");
            var rows2 = new List<(string Mark, string Name)>();

            foreach (var t in pizza2.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
            {
                if (half2Removes.Contains(t, StringComparer.CurrentCultureIgnoreCase))
                    rows2.Add(("-", t));
                else
                    rows2.Add(("x", t));
            }

            foreach (var t in half2Adds)
                rows2.Add(("+", t));

            result.AddRange(FormatMarkedToppings(rows2, "    "));
        }

        return result;
    }

    private static List<string> FormatMarkedToppings(List<(string Mark, string Name)> rows, string indent = "  ")
    {
        var result = new List<string>();
        if (rows.Count == 0) return result;

        const int columns = 3;
        const int colWidth = 18;

        for (int i = 0; i < rows.Count; i += columns)
        {
            var chunk = rows.Skip(i).Take(columns).ToList();
            var line = indent;

            for (int j = 0; j < chunk.Count; j++)
            {
                var text = $"{chunk[j].Mark} {chunk[j].Name}";
                if (j < chunk.Count - 1)
                    line += text.PadRight(colWidth);
                else
                    line += text;
            }

            result.Add(line);
        }

        return result;
    }

    private static void ParseStandardNote(string? note, out List<string> removes, out List<string> adds)
    {
        removes = new List<string>();
        adds = new List<string>();

        if (string.IsNullOrWhiteSpace(note)) return;

        var parts = note.Split('|').Select(x => x.Trim()).ToList();

        foreach (var p in parts)
        {
            if (p.StartsWith("Uten:", StringComparison.CurrentCultureIgnoreCase))
            {
                var s = p.Substring(5).Trim();
                removes = s.Split(',')
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();
            }
            else if (p.StartsWith("Ekstra:", StringComparison.CurrentCultureIgnoreCase))
            {
                var s = p.Substring(7).Trim();
                adds = s.Split(',')
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();
            }
        }
    }

    private static void ParseSplitNote(
        string? note,
        out List<string> half1Removes,
        out List<string> half1Adds,
        out List<string> half2Removes,
        out List<string> half2Adds)
    {
        half1Removes = new List<string>();
        half1Adds = new List<string>();
        half2Removes = new List<string>();
        half2Adds = new List<string>();

        if (string.IsNullOrWhiteSpace(note)) return;

        var parts = note.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToList();

        foreach (var part in parts)
        {
            if (part.StartsWith("1:", StringComparison.CurrentCultureIgnoreCase))
            {
                ParseStandardNote(part.Substring(2).Trim(), out half1Removes, out half1Adds);
            }
            else if (part.StartsWith("2:", StringComparison.CurrentCultureIgnoreCase))
            {
                ParseStandardNote(part.Substring(2).Trim(), out half2Removes, out half2Adds);
            }
        }
    }

    private static Block BuildReceiptBlock(Order order)
    {
        var section = new Section();

        section.Blocks.Add(MonoBlock(new List<string>
        {
            "PizzaXpressen - Halden",
            "Kongens brygge 2, 1767 Halden",
            "halden@pizzaxpressen.no"
        }, true));

        var logo = TryLogoBlock();
        if (logo != null) section.Blocks.Add(logo);

        var scheduled = GetScheduledLocal(order);
        var type = order.DeliveryType == DeliveryType.Delivery ? "Bringes" : "Hentes";
        var typeLine = scheduled != null ? $"{type}: {scheduled:dd.MM.yyyy} kl. {scheduled:HH:mm}" : $"{type}: -";
        section.Blocks.Add(MonoBlock(new List<string> { typeLine, "" }));

        var userNotes = GetUserNotes(order);
        if (!string.IsNullOrWhiteSpace(userNotes))
        {
            section.Blocks.Add(MonoBlock(new List<string> { "Merknader:", userNotes, "" }, true));
        }

        foreach (var it in order.Items)
        {
            var sum = (it.UnitPrice * it.Quantity).ToString("0.00", CultureInfo.InvariantCulture);
            var left = $"{it.Quantity} {it.ItemName}";
            section.Blocks.Add(MonoLine(left, sum));
        }

        var total = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0) });
        section.Blocks.Add(MonoLine("Pris:", total.ToString("0.00", CultureInfo.InvariantCulture), true));

        return section;
    }

    private static Block? TryLogoBlock()
    {
        try
        {
            var img = new Image();
            img.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
            img.Width = 240;
            img.Stretch = Stretch.Uniform;
            img.HorizontalAlignment = HorizontalAlignment.Center;

            var c = new BlockUIContainer(img);
            c.Margin = new Thickness(0, 6, 0, 6);
            return c;
        }
        catch
        {
            return null;
        }
    }

    private static Block BuildDriverBlock(Order order)
    {
        var scheduled = GetScheduledLocal(order);
        var createdLocal = order.CreatedAtUtc.ToLocalTime();

        var c = order.Customer;
        var customer = (c?.Name ?? "-").Trim();
        var phone = (c?.Phone ?? "-").Trim();

        var address = (c?.AddressText ?? "-").Trim();
        if (string.IsNullOrWhiteSpace(address)) address = "-";

        var lines = new List<string>
        {
            "SJÅFØRLAPP",
            $"Inn kl: {createdLocal:HH:mm}",
            scheduled != null ? $"Bringes: {scheduled:HH:mm} {scheduled:dd.MM.yy}" : "Bringes: -",
            "",
            $"Kunde: {customer}",
            $"Tlf:   {phone}",
            $"Adr:   {address}",
            ""
        };

        var userNotes = GetUserNotes(order);
        if (!string.IsNullOrWhiteSpace(userNotes))
        {
            lines.Add("MERKNADER:");
            lines.AddRange(WrapMono(userNotes, 34).Select(x => "  " + x));
            lines.Add("");
        }

        var section = new Section();
        section.Blocks.Add(MonoBlock(lines, true));

        foreach (var it in order.Items.Where(x => x.ItemName != "Kj.tillegg"))
            section.Blocks.Add(MonoLine($"{it.Quantity} {it.ItemName}", ""));

        var total = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0) });
        section.Blocks.Add(MonoLine("Pris:", total.ToString("0.00", CultureInfo.InvariantCulture), true));

        var qrBlock = BuildQrBlockForAddress(address);
        if (qrBlock != null)
        {
            section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0) });
            section.Blocks.Add(qrBlock);
        }

        return section;
    }

    private static string? GetUserNotes(Order order)
    {
        if (string.IsNullOrWhiteSpace(order.Notes)) return null;

        var parts = order.Notes.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (parts.Count == 0) return null;

        parts.RemoveAll(p => Regex.IsMatch(p, @"\d{2}\.\d{2}\.\d{4}\s+\d{2}:\d{2}"));

        if (parts.Count == 0) return null;

        var text = string.Join(" | ", parts).Trim();
        return text.Length == 0 ? null : text;
    }

    private static IEnumerable<string> WrapMono(string text, int width)
    {
        text = (text ?? "").Replace("\r", "").Trim();
        if (text.Length == 0) yield break;

        foreach (var line in text.Split('\n'))
        {
            var s = line.TrimEnd();
            while (s.Length > width)
            {
                yield return s.Substring(0, width);
                s = s.Substring(width);
            }
            if (s.Length > 0) yield return s;
        }
    }

    private static Block? BuildQrBlockForAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        var trimmed = address.Trim();
        if (trimmed == "-" || trimmed.Length < 3) return null;

        var url = "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(trimmed);

        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);

        using var qr = new PngByteQRCode(data);
        var bytes = qr.GetGraphic(pixelsPerModule: 6);

        var img = new BitmapImage();
        img.BeginInit();
        img.StreamSource = new MemoryStream(bytes);
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();

        var image = new Image
        {
            Source = img,
            Width = 90,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var wrap = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        wrap.Children.Add(new TextBlock
        {
            Text = "Skann for kart",
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 2)
        });
        wrap.Children.Add(image);

        return new BlockUIContainer(wrap) { Margin = new Thickness(0) };
    }

    private static Block MonoBlock(List<string> lines, bool boldTitle = false)
    {
        var p = new Paragraph();
        p.Margin = new Thickness(0);

        for (int i = 0; i < lines.Count; i++)
        {
            var run = new Run(lines[i]);
            if (boldTitle && i == 0) run.FontWeight = FontWeights.Bold;
            p.Inlines.Add(run);
            if (i < lines.Count - 1) p.Inlines.Add(new LineBreak());
        }

        return p;
    }

    private static Block MonoLine(string left, string right, bool bold = false)
    {
        left ??= "";
        right ??= "";

        var maxLeft = 34;
        var l = left.Length > maxLeft ? left.Substring(0, maxLeft) : left;
        var pad = Math.Max(1, maxLeft - l.Length);
        var text = l + new string(' ', pad) + right;

        var p = new Paragraph(new Run(text));
        p.Margin = new Thickness(0);
        if (bold) p.FontWeight = FontWeights.Bold;
        return p;
    }

    private static DateTime? GetScheduledLocal(Order order)
    {
        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            var m = Regex.Match(order.Notes, @"(\d{2}\.\d{2}\.\d{4})\s+(\d{2}:\d{2})");
            if (m.Success)
            {
                if (DateTime.TryParseExact(
                        m.Groups[1].Value + " " + m.Groups[2].Value,
                        "dd.MM.yyyy HH:mm",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dt))
                    return dt;
            }
        }
        return null;
    }

    private static double MmToPx(double mm) => (mm / 25.4) * 96.0;
}