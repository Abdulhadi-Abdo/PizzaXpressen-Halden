using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PizzaXpressenHalden.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _nav;

    [ObservableProperty]
    private object? currentViewModel;

    public IRelayCommand GoOrderCommand { get; }
    public IRelayCommand GoPizzaRegisterCommand { get; }
    public IRelayCommand GoOrderLogCommand { get; }

    public MainViewModel(INavigationService nav)
    {
        _nav = nav;
        _nav.CurrentViewModelChanged += () => CurrentViewModel = _nav.CurrentViewModel;

        GoOrderCommand = new RelayCommand(() => _nav.NavigateTo<OrderViewModel>());
        GoPizzaRegisterCommand = new RelayCommand(() => _nav.NavigateTo<PizzaRegisterViewModel>());
        GoOrderLogCommand = new RelayCommand(() => _nav.NavigateTo<OrderLogViewModel>());

        _nav.NavigateTo<OrderViewModel>();
    }
}
