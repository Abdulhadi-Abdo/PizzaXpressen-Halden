using PizzaXpressenHalden.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PizzaXpressenHalden.Views;

public partial class OrderView : UserControl
{
    public OrderView()
    {
        InitializeComponent();

        AddressSuggestionList.PreviewMouseLeftButtonUp += AddressSuggestionList_PreviewMouseLeftButtonUp;
        AddressSuggestionList.PreviewKeyDown += AddressSuggestionList_PreviewKeyDown;

        PreviewKeyDown += OrderView_PreviewKeyDown;
    }

    private void OrderView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused == null)
            return;

        // Merknader skal beholde Enter som ny linje
        if (e.Key == Key.Enter && focused is TextBox notesTb && notesTb.AcceptsReturn)
            return;

        // Adresseforslag skal beholde egen oppførsel
        if (IsDescendantOf(focused, AddressSuggestionList))
            return;

        // Ikke overstyr printknappen
        if (PrintButton != null && IsDescendantOf(focused, PrintButton))
            return;

        // PILER = flytt mellom bokser
        if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
        {
            // Ikke stjel piler i store felt / spesielle felt
            if (focused == NotesBox || IsDescendantOf(focused, NotesBox))
                return;

            if (focused == AddressSuggestionList || IsDescendantOf(focused, AddressSuggestionList))
                return;

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

                element.MoveFocus(new TraversalRequest(direction));
            }

            return;
        }

        if (e.Key != Key.Enter)
            return;

        // ENTER = hopp mellom seksjoner
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
            FocusHandlekurvArea();
            return;
        }

        if (IsDescendantOf(focused, HandlekurvPanel))
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

    private bool IsInBestillingArea(DependencyObject focused)
    {
        return focused == SizeBox ||
               focused == PizzaNrBox ||
               focused == AntallBox ||
               IsDescendantOf(focused, BestillingPanel);
    }

    private static bool IsDescendantOf(DependencyObject? focused, DependencyObject? ancestor)
    {
        if (focused == null || ancestor == null) return false;

        var cur = focused;
        while (cur != null)
        {
            if (ReferenceEquals(cur, ancestor)) return true;
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

    private void FocusHandlekurvArea()
    {
        if (HandlekurvGrid != null && HandlekurvGrid.Items.Count > 0)
        {
            HandlekurvGrid.Focus();
            return;
        }

        if (EditPizzaButton != null && EditPizzaButton.IsEnabled && EditPizzaButton.IsVisible)
        {
            EditPizzaButton.Focus();
            return;
        }

        if (RemoveItemButton != null && RemoveItemButton.IsEnabled && RemoveItemButton.IsVisible)
        {
            RemoveItemButton.Focus();
        }
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
        if (e.Key != Key.Enter) return;

        if (DataContext is OrderViewModel vm && AddressSuggestionList.SelectedItem is string s)
        {
            vm.SelectAddressSuggestion(s);
            e.Handled = true;
            Keyboard.Focus(AddressBox);
        }
    }
}