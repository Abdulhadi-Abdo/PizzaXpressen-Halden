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

        var width = MmToPx(210);
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
        var a4Width = MmToPx(210);
        var a4Height = MmToPx(297);

        var receiptWidth = MmToPx(105);
        var topPadding = 10.0;
        var rightPadding = 10.0;
        var leftPadding = a4Width - receiptWidth - rightPadding;

        var d = new FlowDocument
        {
            PageWidth = a4Width,
            PageHeight = a4Height,
            PagePadding = new Thickness(leftPadding, topPadding, rightPadding, 10),
            ColumnWidth = receiptWidth,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11
        };

        var kitchen = BuildKitchenBlock(order);
        var receipt = BuildReceiptBlock(order);
        var driver = order.DeliveryType == DeliveryType.Delivery ? BuildDriverBlock(order) : null;

        if (kitchen is Section ks) ks.Margin = new Thickness(0, 0, 0, 200);
        if (kitchen is Paragraph kp) kp.Margin = new Thickness(0, 0, 0, 200);

        if (receipt is Section rs) rs.Margin = new Thickness(0, 0, 0, 300);
        if (receipt is Paragraph rp) rp.Margin = new Thickness(0, 0, 0, 300);

        if (driver is Section ds) ds.Margin = new Thickness(0, 0, 0, 0);
        if (driver is Paragraph dp) dp.Margin = new Thickness(0, 0, 0, 0);

        d.Blocks.Add(kitchen);
        d.Blocks.Add(receipt);

        if (driver != null)
            d.Blocks.Add(driver);

        return d;
    }

    private Block BuildKitchenBlock(Order order)
    {
        var scheduled = GetScheduledLocal(order);
        var createdLocal = order.CreatedAtUtc.ToLocalTime();

        var customerName = (order.Customer?.Name ?? "-").Trim();
        var phone = (order.Customer?.Phone ?? "-").Trim();
        var deliveryFlag = order.DeliveryType == DeliveryType.Delivery ? "B" : "H";

        var section = new Section
        {
            Margin = new Thickness(0)
        };

        var headerLines = new List<string>
        {
            "KJØKKENLAPP",
            $"{customerName} / {phone}",
            $"{deliveryFlag}: {order.Id}, Lapp: 1, Inn: {createdLocal:dd.MM HH:mm}, Leveres: {(scheduled != null ? scheduled.Value.ToString("dd.MM HH:mm") : "-")}",
            ""
        };

        section.Blocks.Add(MonoBlock(headerLines, true));

        bool IsPizzaLine(OrderItem it)
            => it.PizzaId != null || (it.ItemName ?? "").StartsWith("DELT ", StringComparison.OrdinalIgnoreCase);

        foreach (var pizzaItem in order.Items.Where(IsPizzaLine))
        {
            section.Blocks.Add(BuildKitchenPizzaTable(pizzaItem));
            section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 2, 0, 2) });
        }

        foreach (var it in order.Items.Where(x => !IsPizzaLine(x) && x.PizzaId == null && x.ItemName != "Kj.tillegg"))
        {
            section.Blocks.Add(MonoBlock(new List<string> { $"{it.Quantity} {DisplayItemName(it.ItemName)}" }, false));
        }

        if (order.Items.Any(x => string.Equals(x.ItemName, "Kj.tillegg", StringComparison.OrdinalIgnoreCase)))
        {
            var fee = order.Items.First(x => string.Equals(x.ItemName, "Kj.tillegg", StringComparison.OrdinalIgnoreCase));
            section.Blocks.Add(MonoBlock(new List<string> { $"{fee.Quantity} Kjøretillegg" }, false));
        }

        return section;
    }

    private Block BuildKitchenPizzaTable(OrderItem item)
    {
        if ((item.ItemName ?? "").StartsWith("DELT ", StringComparison.OrdinalIgnoreCase))
            return BuildKitchenSplitPizzaTable(item);

        ParsePizzaItemName(item.ItemName, out var pizzaNr, out var size, out _);
        ParseStandardNote(item.Note, out var removes, out var adds);

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

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };

        table.Columns.Add(new TableColumn { Width = new GridLength(120) });
        table.Columns.Add(new TableColumn { Width = new GridLength(40) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var header1 = new TableRow();
        header1.Cells.Add(CreateCell("Nr", true, true));
        header1.Cells.Add(CreateCell(string.IsNullOrWhiteSpace(pizzaNr) ? DisplayItemName(item.ItemName) : pizzaNr, true, true));
        group.Rows.Add(header1);

        var header2 = new TableRow();
        header2.Cells.Add(CreateCell("Str", false, true));
        header2.Cells.Add(CreateCell(size, false, true));
        group.Rows.Add(header2);

        foreach (var topping in baseToppings)
        {
            var mark = removes.Contains(topping, StringComparer.CurrentCultureIgnoreCase) ? "-" : "x";
            var row = new TableRow();
            row.Cells.Add(CreateCell(DisplayItemName(topping), false, true));
            row.Cells.Add(CreateCell(mark, false, true));
            group.Rows.Add(row);
        }

        foreach (var topping in adds)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell(DisplayItemName(topping), false, true));
            row.Cells.Add(CreateCell("x", false, true));
            group.Rows.Add(row);
        }

        return table;
    }

    private Block BuildKitchenSplitPizzaTable(OrderItem item)
    {
        var section = new Section
        {
            Margin = new Thickness(0)
        };

        var name = item.ItemName ?? "";
        var m = Regex.Match(name, @"DELT\s+(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase);

        if (!m.Success)
        {
            section.Blocks.Add(MonoBlock(new List<string> { DisplayItemName(name) }, false));
            return section;
        }

        var nr1 = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var nr2 = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

        ParsePizzaItemName(item.ItemName, out _, out var size, out _);
        ParseSplitNote(item.Note, out var half1Removes, out var half1Adds, out var half2Removes, out var half2Adds);

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

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };

        table.Columns.Add(new TableColumn { Width = new GridLength(120) });
        table.Columns.Add(new TableColumn { Width = new GridLength(40) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var header1 = new TableRow();
        header1.Cells.Add(CreateCell("Nr", true, true));
        header1.Cells.Add(CreateCell($"{nr1}/{nr2}", true, true));
        group.Rows.Add(header1);

        var header2 = new TableRow();
        header2.Cells.Add(CreateCell("Str", false, true));
        header2.Cells.Add(CreateCell(size, false, true));
        group.Rows.Add(header2);

        if (pizza1 != null)
        {
            var halfRow = new TableRow();
            halfRow.Cells.Add(CreateCell($"1/2 {pizza1.Navn}", true, true));
            halfRow.Cells.Add(CreateCell("", true, true));
            group.Rows.Add(halfRow);

            foreach (var topping in pizza1.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
            {
                var mark = half1Removes.Contains(topping, StringComparer.CurrentCultureIgnoreCase) ? "-" : "x";
                var row = new TableRow();
                row.Cells.Add(CreateCell(DisplayItemName(topping), false, true));
                row.Cells.Add(CreateCell(mark, false, true));
                group.Rows.Add(row);
            }

            foreach (var topping in half1Adds)
            {
                var row = new TableRow();
                row.Cells.Add(CreateCell(DisplayItemName(topping), false, true));
                row.Cells.Add(CreateCell("x", false, true));
                group.Rows.Add(row);
            }
        }

        if (pizza2 != null)
        {
            var halfRow = new TableRow();
            halfRow.Cells.Add(CreateCell($"1/2 {pizza2.Navn}", true, true));
            halfRow.Cells.Add(CreateCell("", true, true));
            group.Rows.Add(halfRow);

            foreach (var topping in pizza2.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
            {
                var mark = half2Removes.Contains(topping, StringComparer.CurrentCultureIgnoreCase) ? "-" : "x";
                var row = new TableRow();
                row.Cells.Add(CreateCell(DisplayItemName(topping), false, true));
                row.Cells.Add(CreateCell(mark, false, true));
                group.Rows.Add(row);
            }

            foreach (var topping in half2Adds)
            {
                var row = new TableRow();
                row.Cells.Add(CreateCell(DisplayItemName(topping), false, true));
                row.Cells.Add(CreateCell("x", false, true));
                group.Rows.Add(row);
            }
        }

        section.Blocks.Add(table);
        return section;
    }

    private static Block BuildReceiptBlock(Order order)
    {
        var section = new Section
        {
            Margin = new Thickness(0)
        };

        var logo = TryLogoBlock();
        if (logo != null)
            section.Blocks.Add(logo);

        section.Blocks.Add(MonoBlock(new List<string>
        {
            "PizzaXpressen - Halden",
            "Kongens brygge 2, 1767 Halden",
            "halden@pizzaxpressen.no"
        }, true));

        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 2, 0, 2) });

        var scheduled = GetScheduledLocal(order);
        var type = order.DeliveryType == DeliveryType.Delivery ? "Bringes" : "Hentes";
        var typeLine = scheduled != null ? $"{type}: {scheduled:dd.MM.yyyy} kl. {scheduled:HH:mm}" : $"{type}: -";
        section.Blocks.Add(MonoBlock(new List<string> { typeLine, "" }));

        var userNotes = GetUserNotes(order);
        if (!string.IsNullOrWhiteSpace(userNotes))
            section.Blocks.Add(MonoBlock(new List<string> { "Merknader:", userNotes, "" }, true));

        foreach (var it in order.Items)
        {
            var sum = (it.UnitPrice * it.Quantity).ToString("0.00", CultureInfo.InvariantCulture);
            var left = $"{it.Quantity} {DisplayItemName(it.ItemName)}";
            section.Blocks.Add(MonoLine(left, sum));
        }

        var total = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 2, 0, 2) });
        section.Blocks.Add(MonoLine("Pris:", total.ToString("0.00", CultureInfo.InvariantCulture), true));

        return section;
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

        var section = new Section
        {
            Margin = new Thickness(0)
        };

        section.Blocks.Add(MonoBlock(lines, true));

        foreach (var it in order.Items.Where(x => x.ItemName != "Kj.tillegg"))
            section.Blocks.Add(MonoLine($"{it.Quantity} {DisplayItemName(it.ItemName)}", ""));

        var total = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 2, 0, 2) });
        section.Blocks.Add(MonoLine("Pris:", total.ToString("0.00", CultureInfo.InvariantCulture), true));

        var qrBlock = BuildQrBlockForAddress(address);
        if (qrBlock != null)
        {
            section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 4, 0, 4) });
            section.Blocks.Add(qrBlock);
        }

        return section;
    }

    private static TableCell CreateCell(string text, bool bold, bool border)
    {
        var p = new Paragraph(new Run(text ?? ""))
        {
            Margin = new Thickness(2, 1, 2, 1)
        };

        if (bold)
            p.FontWeight = FontWeights.Bold;

        return new TableCell(p)
        {
            Padding = new Thickness(0),
            BorderBrush = border ? Brushes.Black : Brushes.Transparent,
            BorderThickness = border ? new Thickness(0.5) : new Thickness(0)
        };
    }

    private static void ParsePizzaItemName(string? itemName, out string pizzaNr, out string size, out string title)
    {
        pizzaNr = "";
        size = "";
        title = itemName ?? "";

        if (string.IsNullOrWhiteSpace(itemName))
            return;

        var sizeMatch = Regex.Match(itemName, @"\(([^)]+)\)");
        if (sizeMatch.Success)
            size = sizeMatch.Groups[1].Value.Trim();

        var noSize = Regex.Replace(itemName, @"\s*\([^)]+\)\s*$", "").Trim();

        if (noSize.StartsWith("DELT ", StringComparison.OrdinalIgnoreCase))
        {
            pizzaNr = noSize.Replace("DELT ", "", StringComparison.OrdinalIgnoreCase).Trim();
            title = noSize;
            return;
        }

        var match = Regex.Match(noSize, @"^(\d+)\s+(.*)$");
        if (match.Success)
        {
            pizzaNr = match.Groups[1].Value.Trim();
            title = match.Groups[2].Value.Trim();
        }
        else
        {
            title = noSize;
        }
    }

    private static string DisplayItemName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        if (string.Equals(name, "Kj.tillegg", StringComparison.OrdinalIgnoreCase))
            return "Kjøretillegg";

        return name.Trim();
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
                ParseStandardNote(part.Substring(2).Trim(), out half1Removes, out half1Adds);
            else if (part.StartsWith("2:", StringComparison.CurrentCultureIgnoreCase))
                ParseStandardNote(part.Substring(2).Trim(), out half2Removes, out half2Adds);
        }
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

    private static Block MonoBlock(List<string> lines, bool boldTitle = false)
    {
        var p = new Paragraph { Margin = new Thickness(0) };

        for (int i = 0; i < lines.Count; i++)
        {
            var run = new Run(lines[i]);
            if (boldTitle && i == 0) run.FontWeight = FontWeights.Bold;
            p.Inlines.Add(run);
            if (i < lines.Count - 1)
                p.Inlines.Add(new LineBreak());
        }

        return p;
    }

    private static Block MonoLine(string left, string right, bool bold = false)
    {
        left ??= "";
        right ??= "";

        const int maxLeft = 34;
        var l = left.Length > maxLeft ? left.Substring(0, maxLeft) : left;
        var pad = Math.Max(1, maxLeft - l.Length);
        var text = l + new string(' ', pad) + right;

        var p = new Paragraph(new Run(text)) { Margin = new Thickness(0) };
        if (bold) p.FontWeight = FontWeights.Bold;
        return p;
    }

    private static Block? TryLogoBlock()
    {
        try
        {
            var img = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")),
                Width = 240,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            return new BlockUIContainer(img) { Margin = new Thickness(0, 4, 0, 2) };
        }
        catch
        {
            return null;
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
        var bytes = qr.GetGraphic(6);

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

        var wrap = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        wrap.Children.Add(new TextBlock
        {
            Text = "Skann for kart",
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 2)
        });
        wrap.Children.Add(image);

        return new BlockUIContainer(wrap) { Margin = new Thickness(0) };
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