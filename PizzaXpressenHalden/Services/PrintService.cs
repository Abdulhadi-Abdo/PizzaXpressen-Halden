using Microsoft.EntityFrameworkCore;
using PizzaXpressenHalden.Models;
using QRCoder;
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
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        PrintDocument(BuildFixedDocument(full), "PizzaXpressen Halden");
    }

    private static void PrintDocument(FixedDocument doc, string jobName)
    {
        var queue = LocalPrintServer.GetDefaultPrintQueue();

        var width = MmToPx(210);
        var height = MmToPx(297);

        var ticket = queue.DefaultPrintTicket;
        ticket.PageMediaSize = new PageMediaSize(width, height);

        try
        {
            var writer = PrintQueue.CreateXpsDocumentWriter(queue);
            writer.Write(doc, ticket);
        }
        catch
        {
            var dlg = new PrintDialog
            {
                PrintQueue = queue,
                PrintTicket = ticket
            };

            if (dlg.ShowDialog() == true)
                dlg.PrintDocument(doc.DocumentPaginator, jobName);
        }
    }

    private FixedDocument BuildFixedDocument(Order order)
    {
        double pageWidth = MmToPx(210);
        double pageHeight = MmToPx(297);

        double contentWidth = MmToPx(94);
        double left = (pageWidth - contentWidth) / 2.0;

        double kitchenTop = 18;
        double receiptTop = 410;
        double driverTop = 760;

        double maxKitchenHeight = receiptTop - kitchenTop - 10;
        double maxReceiptHeight = driverTop - receiptTop - 10;
        double maxDriverHeight = pageHeight - driverTop - 10;

        double kitchenHeight = Math.Min(CalculateKitchenBlockHeight(order), maxKitchenHeight);
        double receiptHeight = Math.Min(CalculateReceiptBlockHeight(order), maxReceiptHeight);
        double driverHeight = Math.Min(CalculateDriverBlockHeight(order), maxDriverHeight);

        var fixedDoc = new FixedDocument
        {
            DocumentPaginator =
            {
                PageSize = new Size(pageWidth, pageHeight)
            }
        };

        var pageContent = new PageContent();
        var fixedPage = new FixedPage
        {
            Width = pageWidth,
            Height = pageHeight,
            Background = Brushes.White
        };

        var kitchen = WrapFixedRegionUnscaled(BuildKitchenBlock(order), contentWidth, kitchenHeight);
        FixedPage.SetLeft(kitchen, left);
        FixedPage.SetTop(kitchen, kitchenTop);
        fixedPage.Children.Add(kitchen);

        var receipt = WrapFixedRegionUnscaled(BuildReceiptBlock(order), contentWidth, receiptHeight);
        FixedPage.SetLeft(receipt, left);
        FixedPage.SetTop(receipt, receiptTop);
        fixedPage.Children.Add(receipt);

        if (order.DeliveryType == DeliveryType.Delivery)
        {
            var driver = WrapFixedRegionUnscaled(BuildDriverBlock(order), contentWidth, driverHeight);
            FixedPage.SetLeft(driver, left);
            FixedPage.SetTop(driver, driverTop);
            fixedPage.Children.Add(driver);
        }

        ((IAddChild)pageContent).AddChild(fixedPage);
        fixedDoc.Pages.Add(pageContent);

        return fixedDoc;
    }

    private double CalculateKitchenBlockHeight(Order order)
    {
        var pizzaColumns = BuildKitchenPizzaColumns(order);

        var toppings = pizzaColumns
            .SelectMany(x => x.ToppingStates.Keys)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        double baseHeaderHeight = 56;
        double total = 0;

        total += baseHeaderHeight;
        total += 4;
        total += 20; // Nr x Ant
        total += 20; // Str

        foreach (var topping in toppings)
            total += EstimateKitchenLabelRowHeight(DisplayItemName(topping));

        total += 6;

        return Math.Max(245, total);
    }

    private double CalculateReceiptBlockHeight(Order order)
    {
        int pizzaRows = order.Items.Count(IsPizzaItem) + 1;
        int extraRows = order.Items.Count(x => !IsPizzaItem(x)) + 1;

        double total = 0;
        total += 72;
        total += 22;
        total += 8;
        total += Math.Max(pizzaRows, extraRows) * 16;
        total += 10;

        return Math.Max(245, total);
    }

    private double CalculateDriverBlockHeight(Order order)
    {
        var userNotes = GetUserNotes(order);

        int extraLines = order.Items.Count(x =>
            !IsPizzaItem(x) &&
            !string.Equals(x.ItemName, "Kj.tillegg", StringComparison.OrdinalIgnoreCase));

        double total = 0;
        total += 78;
        total += 30;
        total += Math.Max(0, extraLines) * 14;
        total += 40;

        if (!string.IsNullOrWhiteSpace(userNotes))
            total += 18 + (EstimateTextLineCount(userNotes, 26) * 13);

        total += 90;
        total += 10;

        return Math.Max(280, total);
    }

    private static double EstimateKitchenLabelRowHeight(string text)
    {
        int lineCount = EstimateTextLineCount(text, 18);
        return Math.Max(18, (lineCount * 12) + 3);
    }

    private static int EstimateTextLineCount(string text, int charsPerLine)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 1;

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        int total = 0;
        foreach (var line in lines)
        {
            var current = string.IsNullOrEmpty(line) ? " " : line;
            total += Math.Max(1, (int)Math.Ceiling(current.Length / (double)charsPerLine));
        }

        return Math.Max(1, total);
    }

    private static Border WrapFixedRegionUnscaled(UIElement content, double width, double height)
    {
        return new Border
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            Padding = new Thickness(4),
            Child = new Border
            {
                Width = width,
                Background = Brushes.White,
                Child = content
            }
        };
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

    private static string NormalizeSize(string? size)
    {
        var s = (size ?? "").Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(s))
            return "S";

        if (s == "L")
            return "S";

        return s;
    }

    private static string FormatKitchenNrAndQuantity(string numberText, int quantity)
    {
        numberText = (numberText ?? "").Trim();

        if (quantity <= 1)
            return numberText;

        return $"{numberText} x {quantity}";
    }

    private sealed class KitchenPizzaColumn
    {
        public string NumberText { get; set; } = "";
        public string SizeText { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public Dictionary<string, string> ToppingStates { get; set; } =
            new(StringComparer.CurrentCultureIgnoreCase);
    }

    private UIElement BuildKitchenBlock(Order order)
    {
        var scheduled = GetScheduledLocal(order);
        var createdLocal = order.CreatedAtUtc.ToLocalTime();

        var customerName = (order.Customer?.Name ?? "-").Trim();
        var phone = (order.Customer?.Phone ?? "-").Trim();
        var deliveryFlag = order.DeliveryType == DeliveryType.Delivery ? "B" : "H";

        var root = new StackPanel
        {
            Margin = new Thickness(0)
        };

        root.Children.Add(Text("KJØKKENLAPP", true, 12));
        root.Children.Add(Text($"{customerName} / {phone}", false, 11));
        root.Children.Add(Text(
            $"{deliveryFlag}: {order.Id}, Lapp: 1, Inn: {createdLocal:dd.MM HH:mm}, Leveres: {(scheduled != null ? scheduled.Value.ToString("dd.MM HH:mm") : "-")}",
            false, 10));

        root.Children.Add(Spacer(4));

        var pizzaColumns = BuildKitchenPizzaColumns(order);
        if (pizzaColumns.Count > 0)
            root.Children.Add(BuildKitchenCombinedTable(pizzaColumns));

        return root;
    }

    private List<KitchenPizzaColumn> BuildKitchenPizzaColumns(Order order)
    {
        var raw = new List<KitchenPizzaColumn>();

        foreach (var item in order.Items.Where(IsPizzaItem))
        {
            ParsePizzaItemName(item.ItemName, out var pizzaNr, out var size, out _);
            var quantity = Math.Max(1, item.Quantity);

            size = NormalizeSize(size);

            var toppingStates = BuildKitchenToppingStates(item);

            for (int i = 0; i < quantity; i++)
            {
                raw.Add(new KitchenPizzaColumn
                {
                    NumberText = string.IsNullOrWhiteSpace(pizzaNr) ? DisplayItemName(item.ItemName) : pizzaNr,
                    SizeText = size,
                    Quantity = 1,
                    ToppingStates = new Dictionary<string, string>(toppingStates, StringComparer.CurrentCultureIgnoreCase)
                });
            }
        }

        var grouped = raw
            .GroupBy(x => new
            {
                x.NumberText,
                x.SizeText,
                ToppingsKey = string.Join("|",
                    x.ToppingStates
                        .OrderBy(k => k.Key, StringComparer.CurrentCultureIgnoreCase)
                        .Select(k => $"{k.Key}:{k.Value}"))
            })
            .Select(g => new KitchenPizzaColumn
            {
                NumberText = g.Key.NumberText,
                SizeText = g.Key.SizeText,
                Quantity = g.Count(),
                ToppingStates = new Dictionary<string, string>(g.First().ToppingStates, StringComparer.CurrentCultureIgnoreCase)
            })
            .OrderBy(x =>
            {
                if (int.TryParse(x.NumberText, out var nr))
                    return nr;
                return int.MaxValue;
            })
            .ThenBy(x => x.SizeText)
            .ToList();

        return grouped;
    }

    private Dictionary<string, string> BuildKitchenToppingStates(OrderItem item)
    {
        var result = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);

        if ((item.ItemName ?? "").StartsWith("DELT ", StringComparison.OrdinalIgnoreCase))
            return BuildSplitPizzaKitchenToppingStates(item);

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
            result[topping] = "X";

        foreach (var removed in removes)
            result[removed] = "--";

        foreach (var added in adds)
            result[added] = "XX";

        return result;
    }

    private Dictionary<string, string> BuildSplitPizzaKitchenToppingStates(OrderItem item)
    {
        var result = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);

        var name = item.ItemName ?? "";
        var m = Regex.Match(name, @"DELT\s+(\d+)\s*/\s*(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success) return result;

        var nr1 = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var nr2 = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

        ParseSplitNote(item.Note, out var half1Removes, out var half1Adds, out var half2Removes, out var half2Adds);

        var pizza1 = _db.Pizzas
            .AsNoTracking()
            .Include(p => p.PizzaToppings).ThenInclude(pt => pt.Topping)
            .FirstOrDefault(p => p.Nummer == nr1);

        var pizza2 = _db.Pizzas
            .AsNoTracking()
            .Include(p => p.PizzaToppings).ThenInclude(pt => pt.Topping)
            .FirstOrDefault(p => p.Nummer == nr2);

        if (pizza1 != null)
        {
            foreach (var topping in pizza1.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
                result[topping] = "X";

            foreach (var topping in half1Removes)
                result[topping] = "--";

            foreach (var topping in half1Adds)
                result[topping] = "XX";
        }

        if (pizza2 != null)
        {
            foreach (var topping in pizza2.PizzaToppings.OrderBy(x => x.Topping.Sortering).Select(x => x.Topping.Navn))
            {
                if (!result.ContainsKey(topping))
                    result[topping] = "X";
            }

            foreach (var topping in half2Removes)
                result[topping] = "--";

            foreach (var topping in half2Adds)
                result[topping] = "XX";
        }

        return result;
    }

    private UIElement BuildKitchenCombinedTable(List<KitchenPizzaColumn> pizzaColumns)
    {
        var allToppings = pizzaColumns
            .SelectMany(x => x.ToppingStates.Keys)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var grid = new Grid
        {
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        int pizzaCount = Math.Max(1, pizzaColumns.Count);

        double totalTableWidth = 322;
        double firstColumnWidth = 104;
        double pizzaColumnWidth = (totalTableWidth - firstColumnWidth) / pizzaCount;

        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(firstColumnWidth)
        });

        foreach (var _ in pizzaColumns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(pizzaColumnWidth)
            });
        }

        int rowIndex = 0;

        void AddRow(IReadOnlyList<string> cells, bool isHeader)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < cells.Count; i++)
            {
                bool isFirstColumn = i == 0;

                var tb = new TextBlock
                {
                    Text = cells[i],
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = isHeader ? 9.8 : 9.4,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = isFirstColumn && !isHeader ? TextAlignment.Left : TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = isFirstColumn && !isHeader ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    Padding = new Thickness(2, 1.5, 2, 1.5)
                };

                var border = new Border
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1.0),
                    Child = tb
                };

                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, i);
                grid.Children.Add(border);
            }

            rowIndex++;
        }

        AddRow(
            new[] { "Nr x Ant" }
            .Concat(pizzaColumns.Select(x => FormatKitchenNrAndQuantity(x.NumberText, x.Quantity)))
            .ToArray(),
            true);

        AddRow(
            new[] { "Str" }
            .Concat(pizzaColumns.Select(x => NormalizeSize(x.SizeText)))
            .ToArray(),
            true);

        foreach (var topping in allToppings)
        {
            AddRow(
                new[] { DisplayItemName(topping) }
                .Concat(pizzaColumns.Select(x =>
                {
                    if (x.ToppingStates.TryGetValue(topping, out var state))
                        return state;

                    return "";
                }))
                .ToArray(),
                false);
        }

        return grid;
    }

    private UIElement BuildReceiptBlock(Order order)
    {
        var root = new StackPanel();

        var pizzaItems = order.Items.Where(IsPizzaItem).ToList();
        var extraItems = order.Items.Where(x => !IsPizzaItem(x)).ToList();

        int pizzaRowCount = pizzaItems.Count + 1;
        int extraRowCount = extraItems.Count + 1;
        int maxRows = Math.Max(pizzaRowCount, extraRowCount);

        double receiptHeaderFont =
            maxRows <= 8 ? 12 :
            maxRows <= 12 ? 11 : 10.5;

        double receiptBodyFont =
            maxRows <= 8 ? 10 :
            maxRows <= 12 ? 9.2 : 8.6;

        var logo = TryLogoImage();
        if (logo != null)
            root.Children.Add(logo);

        var scheduled = GetScheduledLocal(order);
        var customerName = (order.Customer?.Name ?? "-").Trim();
        var phone = (order.Customer?.Phone ?? "-").Trim();
        var address = (order.Customer?.AddressText ?? "-").Trim();
        if (string.IsNullOrWhiteSpace(address)) address = "-";

        var total = order.Items.Sum(i => i.UnitPrice * i.Quantity);

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftInfo = new StackPanel();
        leftInfo.Children.Add(Text("PizzaXpressen - Halden", true, receiptHeaderFont));
        leftInfo.Children.Add(Text("Kongens brygge 2, 1767 Halden", false, receiptBodyFont));
        leftInfo.Children.Add(Text("halden@pizzaxpressen.no", false, receiptBodyFont));

        var rightInfo = new StackPanel();
        rightInfo.Children.Add(Text($"Kunde: {customerName} / {phone}", true, receiptHeaderFont));
        rightInfo.Children.Add(Text(address, false, receiptBodyFont));
        rightInfo.Children.Add(Text(
            scheduled != null
                ? $"Leveres: {scheduled:dd.MM.yyyy} kl. {scheduled:HH:mm}"
                : "Leveres: -", false, receiptBodyFont));

        Grid.SetColumn(leftInfo, 0);
        Grid.SetColumn(rightInfo, 1);
        headerGrid.Children.Add(leftInfo);
        headerGrid.Children.Add(rightInfo);

        root.Children.Add(headerGrid);
        root.Children.Add(Spacer(5));

        var metaGrid = new Grid();
        metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        var best = Text($"Bestilling: {order.Id} - Lapp: 1", true, receiptHeaderFont);
        var tot = Text($"Total: {total:0.00} kr", true, receiptHeaderFont);
        tot.TextAlignment = TextAlignment.Right;

        Grid.SetColumn(best, 0);
        Grid.SetColumn(tot, 1);
        metaGrid.Children.Add(best);
        metaGrid.Children.Add(tot);

        root.Children.Add(metaGrid);
        root.Children.Add(Spacer(5));

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });

        var pizzaTable = new Grid();
        pizzaTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        pizzaTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        pizzaTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        pizzaTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

        int pRow = 0;
        void AddPizzaRow(string a, string b, string c, string d)
        {
            pizzaTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddGridCell(pizzaTable, pRow, 0, a, receiptBodyFont);
            AddGridCell(pizzaTable, pRow, 1, b, receiptBodyFont);
            AddGridCell(pizzaTable, pRow, 2, c, receiptBodyFont);
            AddGridCell(pizzaTable, pRow, 3, d, receiptBodyFont, TextAlignment.Right);
            pRow++;
        }

        AddPizzaRow("Pizza", "Størrelse", "Ant", "Pris");

        foreach (var item in pizzaItems)
        {
            ParsePizzaItemName(item.ItemName, out _, out var size, out _);
            AddPizzaRow(
                GetReceiptPizzaLabel(item),
                NormalizeSize(size),
                item.Quantity.ToString(),
                (item.UnitPrice * item.Quantity).ToString("0.00", CultureInfo.InvariantCulture));
        }

        var rightSection = new StackPanel();

        var extrasTable = new Grid();
        extrasTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
        extrasTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        extrasTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        int eRow = 0;
        void AddExtraRow(string a, string b, string c)
        {
            extrasTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddGridCell(extrasTable, eRow, 0, a, receiptBodyFont);
            AddGridCell(extrasTable, eRow, 1, b, receiptBodyFont);
            AddGridCell(extrasTable, eRow, 2, c, receiptBodyFont, TextAlignment.Right);
            eRow++;
        }

        AddExtraRow("Tilbehør", "Ant", "Pris");

        foreach (var item in extraItems)
        {
            AddExtraRow(
                DisplayItemName(item.ItemName),
                item.Quantity.ToString(),
                (item.UnitPrice * item.Quantity).ToString("0.00", CultureInfo.InvariantCulture));
        }

        rightSection.Children.Add(extrasTable);

        Grid.SetColumn(pizzaTable, 0);
        Grid.SetColumn(rightSection, 1);
        contentGrid.Children.Add(pizzaTable);
        contentGrid.Children.Add(rightSection);

        root.Children.Add(contentGrid);

        return root;
    }

    private UIElement BuildDriverBlock(Order order)
    {
        var scheduled = GetScheduledLocal(order);
        var createdLocal = order.CreatedAtUtc.ToLocalTime();
        var userNotes = GetUserNotes(order);

        var extraItems = order.Items.Where(x =>
            !IsPizzaItem(x) &&
            !string.Equals(x.ItemName, "Kj.tillegg", StringComparison.OrdinalIgnoreCase)).ToList();

        int driverLineCount = extraItems.Count + (string.IsNullOrWhiteSpace(userNotes) ? 0 : 2);

        double driverTitleFont =
            driverLineCount <= 8 ? 14 :
            driverLineCount <= 12 ? 13 : 12;

        double driverBodyFont =
            driverLineCount <= 8 ? 11 :
            driverLineCount <= 12 ? 10.3 : 9.6;

        var c = order.Customer;
        var customer = (c?.Name ?? "-").Trim();
        var phone = (c?.Phone ?? "-").Trim();
        var address = (c?.AddressText ?? "-").Trim();
        if (string.IsNullOrWhiteSpace(address)) address = "-";

        var total = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        var pizzaCount = CountPizzaQuantity(order);

        var root = new StackPanel();

        root.Children.Add(Text($"Faktura: {order.Id} - Lapp: 1", true, driverTitleFont));
        root.Children.Add(Text($"Kunde: {customer}", false, driverBodyFont));
        root.Children.Add(Text($"Tlf: {phone}", false, driverBodyFont));
        root.Children.Add(Text("Adresse:", false, driverBodyFont));
        root.Children.Add(Text(address, false, driverBodyFont));
        root.Children.Add(Spacer(5));

        root.Children.Add(Text($"## {pizzaCount} Pizza{(pizzaCount == 1 ? "" : "er")} skal bringes", true, driverTitleFont + 1));
        root.Children.Add(Spacer(6));

        foreach (var item in extraItems)
        {
            root.Children.Add(Text($"{item.Quantity}x {DisplayItemName(item.ItemName)}", false, driverBodyFont));
        }

        root.Children.Add(Spacer(8));

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        var leftFooter = new StackPanel();
        leftFooter.Children.Add(Text($"Dato/tid: {createdLocal:dd.MM.yyyy} kl. {createdLocal:HH:mm}", false, driverBodyFont - 1));
        leftFooter.Children.Add(Text(scheduled != null ? $"Levering: {scheduled:HH:mm}" : "Levering: -", false, driverBodyFont - 1));

        var rightFooter = Text($"Total: {total:0.00} kr", true, driverTitleFont);
        rightFooter.TextAlignment = TextAlignment.Right;

        Grid.SetColumn(leftFooter, 0);
        Grid.SetColumn(rightFooter, 1);
        footer.Children.Add(leftFooter);
        footer.Children.Add(rightFooter);

        root.Children.Add(footer);

        if (!string.IsNullOrWhiteSpace(userNotes))
        {
            root.Children.Add(Spacer(6));
            root.Children.Add(Text("Merknad:", true, driverBodyFont));
            root.Children.Add(Text(userNotes, false, driverBodyFont));
        }

        var qr = BuildQrImage(address);
        if (qr != null)
        {
            root.Children.Add(Spacer(6));
            root.Children.Add(Text("Skann for kart", false, driverBodyFont - 1));
            root.Children.Add(qr);
        }

        return root;
    }

    private static void AddGridCell(Grid grid, int row, int col, string text, double fontSize = 10, TextAlignment align = TextAlignment.Left)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = fontSize,
            TextAlignment = align,
            Margin = new Thickness(0),
            TextWrapping = TextWrapping.Wrap
        };

        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    private static TextBlock Text(string text, bool bold = false, double size = 11)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Margin = new Thickness(0),
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static FrameworkElement Spacer(double height)
    {
        return new Border { Height = height, Background = Brushes.Transparent };
    }

    private static Image? TryLogoImage()
    {
        try
        {
            return new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")),
                Width = 210,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            };
        }
        catch
        {
            return null;
        }
    }

    private static Image? BuildQrImage(string address)
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

        return new Image
        {
            Source = img,
            Width = 72,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left
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

        var parts = note.Split('|')
            .Select(x => x.Trim())
            .ToList();

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

        var parts = order.Notes.Split('|')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

        if (parts.Count == 0) return null;

        parts.RemoveAll(p => Regex.IsMatch(p, @"\d{2}\.\d{2}\.\d{4}\s+\d{2}:\d{2}"));

        if (parts.Count == 0) return null;

        var text = string.Join(" | ", parts).Trim();
        return text.Length == 0 ? null : text;
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