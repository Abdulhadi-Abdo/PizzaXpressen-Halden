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

        // Stopper Enter fra å trigge PRINT globalt når du er i input-felter.
        PreviewKeyDown += OrderView_PreviewKeyDown;
    }

    private void OrderView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        // Tillat Enter hvis fokus er på PRINT-knappen (eller inni den)
        if (PrintButton != null && IsDescendantOf(Keyboard.FocusedElement as DependencyObject, PrintButton))
            return;

        // Tillat Enter i forslag-lista (den håndteres i egen handler)
        if (IsDescendantOf(Keyboard.FocusedElement as DependencyObject, AddressSuggestionList))
            return;

        // Ellers: Enter i felter skal IKKE printe / ikke trigge kommandoer
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
            e.Handled = true; // veldig viktig: hindrer at Enter går videre og printer
            Keyboard.Focus(AddressBox);
        }
    }
}
