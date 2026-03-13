using Microsoft.Extensions.DependencyInjection;

namespace PizzaXpressenHalden;

public class NavigationService : INavigationService
{
    private readonly System.IServiceProvider _sp;

    public NavigationService(System.IServiceProvider sp) => _sp = sp;

    public object CurrentViewModel { get; private set; } = null!;
    public event System.Action? CurrentViewModelChanged;

    public void NavigateTo<TViewModel>() where TViewModel : class
    {
        CurrentViewModel = _sp.GetRequiredService<TViewModel>();
        CurrentViewModelChanged?.Invoke();
    }
}
