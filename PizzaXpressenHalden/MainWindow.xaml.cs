using System.Windows;
using System.Windows.Input;
using PizzaXpressenHalden.ViewModels;

namespace PizzaXpressenHalden;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowState = System.Windows.WindowState.Maximized;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.F5)
        {
            vm.GoOrderCommand.Execute(null);
            e.Handled = true;
        }
    }
}
