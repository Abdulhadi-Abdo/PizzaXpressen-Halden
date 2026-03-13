namespace PizzaXpressenHalden;

public interface INavigationService
{
    object CurrentViewModel { get; }
    event System.Action? CurrentViewModelChanged;
    void NavigateTo<TViewModel>() where TViewModel : class;
}
