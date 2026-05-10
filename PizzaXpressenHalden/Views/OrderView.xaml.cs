using PizzaXpressenHalden.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PizzaXpressenHalden.Views;

public partial class OrderView : UserControl
{
    public OrderView()
    {
        InitializeComponent();

        Loaded += OrderView_Loaded;
        PreviewKeyDown += OrderView_PreviewKeyDown;

        AddressSuggestionList.PreviewMouseLeftButtonUp += AddressSuggestionList_PreviewMouseLeftButtonUp;
        AddressSuggestionList.PreviewKeyDown += AddressSuggestionList_PreviewKeyDown;

        if (PrintButton != null)
            PrintButton.Click += PrintButton_Click;
    }

    private void OrderView_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var tb in FindVisualChildren<TextBox>(this))
        {
            if (tb == NotesBox)
                continue;

            tb.GotKeyboardFocus -= TextBox_GotKeyboardFocus_SelectAll;
            tb.GotKeyboardFocus += TextBox_GotKeyboardFocus_SelectAll;

            tb.PreviewMouseLeftButtonDown -= TextBox_PreviewMouseLeftButtonDown_SelectivelyIgnore;
            tb.PreviewMouseLeftButtonDown += TextBox_PreviewMouseLeftButtonDown_SelectivelyIgnore;
        }

        if (HandlekurvGrid != null)
            HandlekurvGrid.PreviewKeyDown += HandlekurvGrid_PreviewKeyDown;

        FocusPizzaNrBox();
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            FocusPizzaNrBox();
        }), DispatcherPriority.ApplicationIdle);
    }

    private void FocusPizzaNrBox()
    {
        if (PizzaNrBox == null)
            return;

        PizzaNrBox.Focus();
        PizzaNrBox.SelectAll();
    }

    private void FocusHandlekurvForEditing()
    {
        if (DataContext is not OrderViewModel vm || HandlekurvGrid == null)
            return;

        var pizzaItems = vm.Items.Where(x => x.PizzaId != null).ToList();
        if (pizzaItems.Count == 0)
            return;

        if (vm.SelectedItem == null || vm.SelectedItem.PizzaId == null)
            vm.SelectedItem = pizzaItems.LastOrDefault();

        HandlekurvGrid.Focus();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            HandlekurvGrid.SelectedItem = vm.SelectedItem;
            HandlekurvGrid.ScrollIntoView(vm.SelectedItem);

            if (HandlekurvGrid.Columns.Count > 0 && vm.SelectedItem != null)
                HandlekurvGrid.CurrentCell = new DataGridCellInfo(vm.SelectedItem, HandlekurvGrid.Columns[0]);
        }), DispatcherPriority.ApplicationIdle);
    }

    private void EditSelectedPizzaFromHandlekurv()
    {
        if (DataContext is not OrderViewModel vm)
            return;

        if (vm.SelectedItem == null || vm.SelectedItem.PizzaId == null)
            return;

        if (vm.EditSelectedPizzaCommand.CanExecute(null))
            vm.EditSelectedPizzaCommand.Execute(null);
    }

    private void ConfirmAndClearEntireOrder()
    {
        if (DataContext is not OrderViewModel vm)
            return;

        bool hasRealContent =
            vm.Items.Any(x => x.ItemName != "Kj.tillegg") ||
            !string.IsNullOrWhiteSpace(vm.Phone) ||
            !string.IsNullOrWhiteSpace(vm.CustomerName) ||
            !string.IsNullOrWhiteSpace(vm.AddressText) ||
            !string.IsNullOrWhiteSpace(vm.OrderNotesText) ||
            !string.IsNullOrWhiteSpace(vm.PizzaNrText) ||
            !string.IsNullOrWhiteSpace(vm.SizeText);

        if (!hasRealContent)
        {
            vm.ClearEntireOrder();
            FocusPizzaNrBox();
            return;
        }

        var result = MessageBox.Show(
            "Er du sikker på at du vil slette hele bestillingen?",
            "Slett hele bestillingen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        vm.ClearEntireOrder();
        FocusPizzaNrBox();
    }

    private void HandlekurvGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not OrderViewModel)
            return;

        if (e.Key == Key.PageUp)
        {
            e.Handled = true;
            EditSelectedPizzaFromHandlekurv();
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            EditSelectedPizzaFromHandlekurv();
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ConfirmAndClearEntireOrder();
            return;
        }

        if (e.Key == Key.Left || e.Key == Key.Right)
        {
            e.Handled = true;
            return;
        }
    }

    private void TextBox_GotKeyboardFocus_SelectAll(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb && !tb.AcceptsReturn)
            tb.SelectAll();
    }

    private void TextBox_PreviewMouseLeftButtonDown_SelectivelyIgnore(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox tb)
            return;

        if (!tb.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            tb.Focus();
        }
    }

    private void OrderView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused == null)
            return;

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ConfirmAndClearEntireOrder();
            return;
        }

        if (focused is TextBox notesTb && notesTb.AcceptsReturn && e.Key == Key.Enter)
            return;

        if (IsDescendantOf(focused, AddressSuggestionList))
            return;

        if (PrintButton != null && IsDescendantOf(focused, PrintButton))
        {
            if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                e.Handled = true;

            return;
        }

        if (e.Key == Key.PageDown)
        {
            if (DataContext is OrderViewModel vm && vm.AddOrUpdatePizzaCommand.CanExecute(null))
            {
                e.Handled = true;
                vm.AddOrUpdatePizzaCommand.Execute(null);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    FocusPizzaNrBox();
                }), DispatcherPriority.ApplicationIdle);
            }
            return;
        }

        if (e.Key == Key.PageUp)
        {
            e.Handled = true;

            if (IsDescendantOf(focused, HandlekurvPanel) || focused == HandlekurvGrid)
                EditSelectedPizzaFromHandlekurv();
            else
                FocusHandlekurvForEditing();

            return;
        }

        if (e.Key == Key.F12)
        {
            if (DataContext is OrderViewModel vm && vm.SaveAndPrintCommand.CanExecute(null))
                vm.SaveAndPrintCommand.Execute(null);

            e.Handled = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                FocusPizzaNrBox();
            }), DispatcherPriority.ApplicationIdle);

            return;
        }

        if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
        {
            if (focused == NotesBox || IsDescendantOf(focused, NotesBox))
                return;

            if (IsDescendantOf(focused, HandlekurvPanel))
                return;

            if (focused is TextBox tb && !tb.AcceptsReturn)
            {
                var textLength = tb.Text?.Length ?? 0;
                var caret = tb.CaretIndex;
                var hasSelection = tb.SelectionLength > 0;

                if (e.Key == Key.Left)
                {
                    if (hasSelection)
                        return;

                    if (caret > 0)
                        return;
                }

                if (e.Key == Key.Right)
                {
                    if (hasSelection)
                        return;

                    if (caret < textLength)
                        return;
                }
            }

            e.Handled = true;

            if (focused is UIElement element)
            {
                var direction = e.Key switch
                {
                    Key.Left => FocusNavigationDirection.Left,
                    Key.Right => FocusNavigationDirection.Right,
                    Key.Up => FocusNavigationDirection.Up,
                    Key.Down => FocusNavigationDirection.Down,
                    _ => FocusNavigationDirection.Next
                };

                MoveFocusSkippingHandlekurv(element, direction);
            }

            return;
        }

        if (e.Key != Key.Enter)
            return;

        if (IsInBestillingArea(focused))
        {
            e.Handled = true;
            FocusFirstEditableTextBox(ToppingPanel);
            return;
        }

        if (IsDescendantOf(focused, ToppingPanel))
        {
            e.Handled = true;
            FocusFirstEditableTextBox(TilbehorPanel);
            return;
        }

        if (IsDescendantOf(focused, TilbehorPanel))
        {
            e.Handled = true;
            FocusFirstEditableTextBox(KundePanel);
            return;
        }

        if (IsDescendantOf(focused, KundePanel))
        {
            e.Handled = true;
            NotesBox.Focus();
            NotesBox.CaretIndex = NotesBox.Text?.Length ?? 0;
            return;
        }

        if (focused == NotesBox || IsDescendantOf(focused, NotesBox))
        {
            e.Handled = true;
            PrintButton.Focus();
        }
    }

    private void MoveFocusSkippingHandlekurv(UIElement startElement, FocusNavigationDirection direction)
    {
        var current = startElement;

        for (int i = 0; i < 20; i++)
        {
            var request = new TraversalRequest(direction);
            if (!current.MoveFocus(request))
                return;

            if (Keyboard.FocusedElement is not DependencyObject newFocused)
                return;

            if (IsDescendantOf(newFocused, HandlekurvPanel))
            {
                current = newFocused as UIElement ?? current;
                continue;
            }

            SelectAllIfTextBox(Keyboard.FocusedElement);
            return;
        }
    }

    private bool IsInBestillingArea(DependencyObject focused)
    {
        return focused == SizeBox ||
               focused == PizzaNrBox ||
               focused == AntallBox ||
               IsDescendantOf(focused, BestillingPanel);
    }

    private static bool IsDescendantOf(DependencyObject? focused, DependencyObject? ancestor)
    {
        if (focused == null || ancestor == null)
            return false;

        var cur = focused;
        while (cur != null)
        {
            if (ReferenceEquals(cur, ancestor))
                return true;

            cur = VisualTreeHelper.GetParent(cur);
        }

        return false;
    }

    private static void FocusFirstEditableTextBox(DependencyObject root)
    {
        var first = FindVisualChildren<TextBox>(root)
            .FirstOrDefault(x => x.IsEnabled && x.IsVisible && !x.AcceptsReturn);

        if (first != null)
        {
            first.Focus();
            first.SelectAll();
        }
    }

    private static void SelectAllIfTextBox(object? focusedElement)
    {
        if (focusedElement is TextBox tb && !tb.AcceptsReturn)
            tb.SelectAll();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj == null)
            yield break;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);

            if (child is T typedChild)
                yield return typedChild;

            foreach (var childOfChild in FindVisualChildren<T>(child))
                yield return childOfChild;
        }
    }

    private void AddressSuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OrderViewModel vm && AddressSuggestionList.SelectedItem is string s)
        {
            vm.SelectAddressSuggestion(s);
            Keyboard.Focus(AddressBox);
        }
    }

    private void AddressSuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (DataContext is OrderViewModel vm && AddressSuggestionList.SelectedItem is string s)
        {
            vm.SelectAddressSuggestion(s);
            e.Handled = true;
            Keyboard.Focus(AddressBox);
        }
    }

    private void DeliveryTypeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var text = (e.Text ?? "").ToUpperInvariant();
        e.Handled = text != "B" && text != "H";
    }

    private void DeliveryTypeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb)
            return;

        if (e.Key == Key.Space)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Tab ||
            e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Home || e.Key == Key.End)
            return;

        if (e.Key == Key.Enter)
            return;

        var allowed = e.Key == Key.B || e.Key == Key.H;

        if (!allowed)
            e.Handled = true;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var value = (tb.Text ?? "").ToUpperInvariant();

            if (value.Length > 1)
                value = value.Substring(0, 1);

            if (value.Length == 1 && value != "B" && value != "H")
                value = "";

            if (tb.Text != value)
            {
                tb.Text = value;
                tb.CaretIndex = tb.Text.Length;
            }
        }), DispatcherPriority.Background);
    }
}