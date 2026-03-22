using PizzaXpressenHalden.ViewModels;
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
        if (e.Key != Key.Enter) return;

        if (PrintButton != null && IsDescendantOf(Keyboard.FocusedElement as DependencyObject, PrintButton))
            return;

        if (IsDescendantOf(Keyboard.FocusedElement as DependencyObject, AddressSuggestionList))
            return;

        var focused = Keyboard.FocusedElement;
        if (focused is TextBox ||
            focused is ComboBox ||
            focused is ListBox ||
            focused is ListBoxItem ||
            focused is DataGrid ||
            focused is DataGridCell ||
            focused is DataGridRow)
        {
            e.Handled = true;
        }
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