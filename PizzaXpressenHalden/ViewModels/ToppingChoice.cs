using CommunityToolkit.Mvvm.ComponentModel;
using PizzaXpressenHalden.Models;

namespace PizzaXpressenHalden.ViewModels;

public enum ToppingState
{
	None,
	Added,
	Removed
}

public partial class ToppingChoice : ObservableObject
{
	public ToppingChoice(Topping topping)
	{
		Topping = topping;
		Name = topping.Navn;
	}

	public Topping Topping { get; }
	public string Name { get; }

	[ObservableProperty] private bool isBase;
	[ObservableProperty] private ToppingState state;

	public void ResetToBase(bool isBase)
	{
		IsBase = isBase;
		State = ToppingState.None;
	}

	public void Toggle()
	{
		if (IsBase)
		{
			State = State == ToppingState.Removed ? ToppingState.None : ToppingState.Removed;
			return;
		}

		State = State == ToppingState.Added ? ToppingState.None : ToppingState.Added;
	}
}
