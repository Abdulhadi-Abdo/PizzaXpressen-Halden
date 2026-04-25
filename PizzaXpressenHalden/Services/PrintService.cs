using Microsoft.EntityFrameworkCore;
using PizzaXpressenHalden.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Printing;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

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

        var horizontalPadding = (a4Width - receiptWidth) / 2.0;
        var topPadding = 10.0;
        var bottomPadding = 10.0;

        var d = new FlowDocument
        {
            PageWidth = a4Width,
            PageHeight = a4Height,
            PagePadding = new Thickness(horizontalPadding, topPadding, horizontalPadding, bottomPadding),
            ColumnWidth = receiptWidth,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11
        };

        var kitchen = BuildKitchenBlock(order);
        var receipt = BuildReceiptBlock(order);
        var driver = order.DeliveryType == DeliveryType.Delivery ? BuildDriverBlock(order) : null;

        if (kitchen is Section ks) ks.Margin = new Thickness(0, 0, 0, 18);
        if (receipt is Section rs) rs.Margin = new Thickness(0, 0, 0, 24);

        d.Blocks.Add(kitchen);
        d.Blocks.Add(receipt);

        if (driver != null)
            d.Blocks.Add(driver);

        return d;
    }

    private static bool IsPizzaItem(OrderItem it)
    {
        return it.PizzaId != null ||
               (it.ItemName ?? "").StartsWith("DELT ", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountPizzaQuantity(Order order)
    {
        return order.Items.Where(IsPizzaItem).Sum(x => x.Quantity);
    }

    private static string GetReceiptPizzaLabel(OrderItem item)
    {
        ParsePizzaItemName(item.ItemName, out var pizzaNr, out _, out _);

        if ((item.ItemName ?? "").StartsWith("DELT ", StringComparison.OrdinalIgnoreCase))
            return $"D{pizzaNr}";

        if (!string.IsNullOrWhiteSpace(pizzaNr))
            return $"#{pizzaNr}";

        return DisplayItemName(item.ItemName);
    }

    private sealed class KitchenPizzaColumn
    {
        public string NumberText { get; set; } = "";
        public string SizeText { get; set; } = "";
        public HashSet<string> Toppings { get; set; } = new(StringComparer.CurrentCultureIgnoreCase);
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

        var pizzaColumns = BuildKitchenPizzaColumns(order);
        if (pizzaColumns.Count > 0)
            section.Blocks.Add(BuildKitchenCombinedTable(pizzaColumns));

        var nonPizzaLines = order.Items
            .Where(x => !IsPizzaItem(x) && !string.Equals(x.ItemName, "Kj.tillegg", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (nonPizzaLines.Count > 0 || order.Items.Any(x => string.Equals(x.ItemName, "Kj.tillegg", StringComparison.OrdinalIgnoreCase)))
            section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 4, 0, 4) });

        foreach (var it in nonPizzaLines)
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

    private List<KitchenPizzaColumn> BuildKitchenPizzaColumns(Order order)
    {
        var result = new List<KitchenPizzaColumn>();

        foreach (var item in order.Items.Where(IsPizzaItem))
        {
            ParsePizzaItemName(item.ItemName, out var pizzaNr, out var size, out _);
            var quantity = Math.Max(1, item.Quantity);

            var toppingSet = BuildKitchenToppingSet(item);

            for (int i = 0; i < quantity; i++)
            {
                result.Add(new KitchenPizzaColumn
                {
                    NumberText = string.IsNullOrWhiteSpace(pizzaNr) ? DisplayItemName(item.ItemName) : pizzaNr,
                    SizeText = size,
                    Toppings = new HashSet<string>(toppingSet, StringComparer.CurrentCultureIgnoreCase)
                });
            }
        }

        return result;
    }

    private HashSet<string> BuildKitchenToppingSet(OrderItem item)
    {
        var result = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        if ((item.ItemName ?? "").StartsWith("DELT ", StringComparison.OrdinalIgnoreCase))
        {
            var splitSet = BuildSplitPizzaKitchenToppingSet(item);
            foreach (var t in splitSet)
                result.Add(t);

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

        foreach (var topping in baseToppings)
        {
            if (!removes.Contains(topping, StringComparer.CurrentCultureIgnoreCase))
                result.Add(topping);
        }

        foreach (var topping in adds)
            result.Add(topping);

        return result;
    }

    private HashSet<string> BuildSplitPizzaKitchenToppingSet(OrderItem item)
    {
        var result = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        var name = item.ItemName ?? "";
        var m = Regex.Match(name, @"DELT\s+(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            return result;

        var nr1 = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var nr2 = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

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

        if (pizza1 != null)
        {
            foreach (var topping in pizza1.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
            {
                if (!half1Removes.Contains(topping, StringComparer.CurrentCultureIgnoreCase))
                    result.Add(topping);
            }

            foreach (var topping in half1Adds)
                result.Add(topping);
        }

        if (pizza2 != null)
        {
            foreach (var topping in pizza2.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
            {
                if (!half2Removes.Contains(topping, StringComparer.CurrentCultureIgnoreCase))
                    result.Add(topping);
            }

            foreach (var topping in half2Adds)
                result.Add(topping);
        }

        return result;
    }

    private Block BuildKitchenCombinedTable(List<KitchenPizzaColumn> pizzaColumns)
    {
        var allToppings = pizzaColumns
            .SelectMany(x => x.Toppings)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };

        table.Columns.Add(new TableColumn { Width = new GridLength(90) });

        foreach (var _ in pizzaColumns)
            table.Columns.Add(new TableColumn { Width = new GridLength(18) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var nrRow = new TableRow();
        nrRow.Cells.Add(CreateCell("Nr", true, true));
        foreach (var pizza in pizzaColumns)
            nrRow.Cells.Add(CreateCell(pizza.NumberText, false, true));
        group.Rows.Add(nrRow);

        var sizeRow = new TableRow();
        sizeRow.Cells.Add(CreateCell("Str", false, true));
        foreach (var pizza in pizzaColumns)
            sizeRow.Cells.Add(CreateCell(pizza.SizeText, false, true));
        group.Rows.Add(sizeRow);

        foreach (var topping in allToppings)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell(DisplayItemName(topping), false, true));

            foreach (var pizza in pizzaColumns)
            {
                var mark = pizza.Toppings.Contains(topping) ? "x" : "";
                row.Cells.Add(CreateCell(mark, false, true));
            }

            group.Rows.Add(row);
        }

        return table;
    }

    private Block BuildReceiptBlock(Order order)
    {
        var section = new Section
        {
            Margin = new Thickness(0)
        };

        var logo = TryLogoBlock();
        if (logo != null)
            section.Blocks.Add(logo);

        var scheduled = GetScheduledLocal(order);
        var customerName = (order.Customer?.Name ?? "-").Trim();
        var phone = (order.Customer?.Phone ?? "-").Trim();
        var address = (order.Customer?.AddressText ?? "-").Trim();
        if (string.IsNullOrWhiteSpace(address)) address = "-";

        var total = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        var userNotes = GetUserNotes(order);

        var header = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };

        header.Columns.Add(new TableColumn { Width = new GridLength(180) });
        header.Columns.Add(new TableColumn { Width = new GridLength(180) });

        var headerGroup = new TableRowGroup();
        header.RowGroups.Add(headerGroup);

        var headerRow = new TableRow();

        var leftInfo = new Section();
        leftInfo.Blocks.Add(CreateParagraph("PizzaXpressen - Halden", true));
        leftInfo.Blocks.Add(CreateParagraph("Kongens brygge 2, 1767 Halden"));
        leftInfo.Blocks.Add(CreateParagraph("halden@pizzaxpressen.no"));

        var rightInfo = new Section();
        rightInfo.Blocks.Add(CreateParagraph($"Kunde: {customerName} / {phone}", true));
        rightInfo.Blocks.Add(CreateParagraph(address));
        rightInfo.Blocks.Add(CreateParagraph(
            scheduled != null
                ? $"Leveres: {scheduled:dd.MM.yyyy} kl. {scheduled:HH:mm}"
                : "Leveres: -"));

        headerRow.Cells.Add(CreatePlainCell(leftInfo));
        headerRow.Cells.Add(CreatePlainCell(rightInfo));
        headerGroup.Rows.Add(headerRow);

        section.Blocks.Add(header);
        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 4, 0, 4) });

        var meta = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };

        meta.Columns.Add(new TableColumn { Width = new GridLength(220) });
        meta.Columns.Add(new TableColumn { Width = new GridLength(140) });

        var metaGroup = new TableRowGroup();
        meta.RowGroups.Add(metaGroup);

        var metaRow = new TableRow();
        metaRow.Cells.Add(CreatePlainCell($"Bestilling: {order.Id} - Lapp: 1", true));
        metaRow.Cells.Add(CreatePlainCell($"Total: {total:0.00} kr", true, TextAlignment.Right));
        metaGroup.Rows.Add(metaRow);

        section.Blocks.Add(meta);
        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 4, 0, 4) });

        var content = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };

        content.Columns.Add(new TableColumn { Width = new GridLength(205) });
        content.Columns.Add(new TableColumn { Width = new GridLength(155) });

        var contentGroup = new TableRowGroup();
        content.RowGroups.Add(contentGroup);

        var contentRow = new TableRow();

        var pizzaTable = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };

        pizzaTable.Columns.Add(new TableColumn { Width = new GridLength(55) });
        pizzaTable.Columns.Add(new TableColumn { Width = new GridLength(55) });
        pizzaTable.Columns.Add(new TableColumn { Width = new GridLength(30) });
        pizzaTable.Columns.Add(new TableColumn { Width = new GridLength(55) });

        var pizzaGroup = new TableRowGroup();
        pizzaTable.RowGroups.Add(pizzaGroup);

        var pizzaHeader = new TableRow();
        pizzaHeader.Cells.Add(CreatePlainCell("Pizza", true));
        pizzaHeader.Cells.Add(CreatePlainCell("Størrelse", true));
        pizzaHeader.Cells.Add(CreatePlainCell("Ant", true));
        pizzaHeader.Cells.Add(CreatePlainCell("Pris", true, TextAlignment.Right));
        pizzaGroup.Rows.Add(pizzaHeader);

        foreach (var item in order.Items.Where(IsPizzaItem))
        {
            ParsePizzaItemName(item.ItemName, out _, out var size, out _);

            var row = new TableRow();
            row.Cells.Add(CreatePlainCell(GetReceiptPizzaLabel(item)));
            row.Cells.Add(CreatePlainCell(size));
            row.Cells.Add(CreatePlainCell(item.Quantity.ToString()));
            row.Cells.Add(CreatePlainCell(
                (item.UnitPrice * item.Quantity).ToString("0.00", CultureInfo.InvariantCulture),
                false,
                TextAlignment.Right));
            pizzaGroup.Rows.Add(row);
        }

        contentRow.Cells.Add(CreatePlainCell(pizzaTable));

        var rightSection = new Section
        {
            Margin = new Thickness(0)
        };

        if (!string.IsNullOrWhiteSpace(userNotes))
        {
            rightSection.Blocks.Add(CreateParagraph("Merknader:", true));
            rightSection.Blocks.Add(CreateParagraph(userNotes));
            rightSection.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 4, 0, 4) });
        }

        var extrasTable = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };

        extrasTable.Columns.Add(new TableColumn { Width = new GridLength(75) });
        extrasTable.Columns.Add(new TableColumn { Width = new GridLength(25) });
        extrasTable.Columns.Add(new TableColumn { Width = new GridLength(45) });

        var extrasGroup = new TableRowGroup();
        extrasTable.RowGroups.Add(extrasGroup);

        var extrasHeader = new TableRow();
        extrasHeader.Cells.Add(CreatePlainCell("Tilbehør/Drikke", true));
        extrasHeader.Cells.Add(CreatePlainCell("Ant", true));
        extrasHeader.Cells.Add(CreatePlainCell("Pris", true, TextAlignment.Right));
        extrasGroup.Rows.Add(extrasHeader);

        foreach (var item in order.Items.Where(x => !IsPizzaItem(x)))
        {
            var row = new TableRow();
            row.Cells.Add(CreatePlainCell(DisplayItemName(item.ItemName)));
            row.Cells.Add(CreatePlainCell(item.Quantity.ToString()));
            row.Cells.Add(CreatePlainCell(
                (item.UnitPrice * item.Quantity).ToString("0.00", CultureInfo.InvariantCulture),
                false,
                TextAlignment.Right));
            extrasGroup.Rows.Add(row);
        }

        rightSection.Blocks.Add(extrasTable);
        contentRow.Cells.Add(CreatePlainCell(rightSection));
        contentGroup.Rows.Add(contentRow);

        section.Blocks.Add(content);
        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 8, 0, 8) });

        section.Blocks.Add(CreateParagraph(
            "Takk for bestillingen. Håper det smaker.",
            true,
            TextAlignment.Center,
            12));

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

        var total = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        var pizzaCount = CountPizzaQuantity(order);

        var section = new Section
        {
            Margin = new Thickness(0)
        };

        section.Blocks.Add(CreateParagraph($"Faktura: {order.Id} - Lapp: 1", true, TextAlignment.Left, 15));
        section.Blocks.Add(CreateParagraph($"Kunde: {customer}"));
        section.Blocks.Add(CreateParagraph($"Tlf: {phone}"));
        section.Blocks.Add(CreateParagraph("Adresse:"));
        section.Blocks.Add(CreateParagraph(address));
        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 4, 0, 4) });

        section.Blocks.Add(CreateParagraph(
            $"## {pizzaCount} Pizza{(pizzaCount == 1 ? "" : "er")} skal bringes",
            true,
            TextAlignment.Left,
            18));

        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 6, 0, 6) });

        foreach (var item in order.Items.Where(x =>
                     !IsPizzaItem(x) &&
                     !string.Equals(x.ItemName, "Kj.tillegg", StringComparison.OrdinalIgnoreCase)))
        {
            section.Blocks.Add(CreateParagraph($"{item.Quantity}x {DisplayItemName(item.ItemName)}", false, TextAlignment.Left, 13));
        }

        section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 10, 0, 10) });

        var footer = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };

        footer.Columns.Add(new TableColumn { Width = new GridLength(200) });
        footer.Columns.Add(new TableColumn { Width = new GridLength(120) });

        var footerGroup = new TableRowGroup();
        footer.RowGroups.Add(footerGroup);

        var footerRow = new TableRow();

        var leftFooter = new Section();
        leftFooter.Blocks.Add(CreateParagraph($"Dato/tid: {createdLocal:dd.MM.yyyy} kl. {createdLocal:HH:mm}"));
        leftFooter.Blocks.Add(CreateParagraph(
            scheduled != null
                ? $"Levering: {scheduled:HH:mm}"
                : "Levering: -"));

        footerRow.Cells.Add(CreatePlainCell(leftFooter));
        footerRow.Cells.Add(CreatePlainCell($"Total: {total:0.00} kr", true, TextAlignment.Right, 15));

        footerGroup.Rows.Add(footerRow);
        section.Blocks.Add(footer);

        var qrBlock = BuildQrBlockForAddress(address);
        if (qrBlock != null)
        {
            section.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 6, 0, 4) });
            section.Blocks.Add(qrBlock);
        }

        return section;
    }

    private static Paragraph CreateParagraph(
        string text,
        bool bold = false,
        TextAlignment alignment = TextAlignment.Left,
        double fontSize = 11)
    {
        var p = new Paragraph(new Run(text ?? ""))
        {
            Margin = new Thickness(0),
            TextAlignment = alignment,
            FontSize = fontSize
        };

        if (bold)
            p.FontWeight = FontWeights.Bold;

        return p;
    }

    private static TableCell CreatePlainCell(Block block)
    {
        var cell = new TableCell
        {
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0)
        };

        cell.Blocks.Add(block);
        return cell;
    }

    private static TableCell CreatePlainCell(
        string text,
        bool bold = false,
        TextAlignment alignment = TextAlignment.Left,
        double fontSize = 11)
    {
        return CreatePlainCell(CreateParagraph(text, bold, alignment, fontSize));
    }

    private static TableCell CreateCell(string text, bool bold, bool border)
    {
        var p = new Paragraph(new Run(text ?? ""))
        {
            Margin = new Thickness(2, 1, 2, 1),
            TextAlignment = TextAlignment.Center
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

            if (s.Length > 0)
                yield return s;
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