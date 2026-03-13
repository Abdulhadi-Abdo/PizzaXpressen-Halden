using Microsoft.EntityFrameworkCore;
using PizzaXpressenHalden.Models;

namespace PizzaXpressenHalden;

public class AppDbContext : DbContext
{
	public DbSet<Pizza> Pizzas => Set<Pizza>();
	public DbSet<Topping> Toppings => Set<Topping>();
	public DbSet<PizzaTopping> PizzaToppings => Set<PizzaTopping>();
	public DbSet<Drink> Drinks => Set<Drink>();
	public DbSet<Extra> Extras => Set<Extra>();
	public DbSet<Tilbehor> Tilbehor => Set<Tilbehor>();
	public DbSet<Customer> Customers => Set<Customer>();
	public DbSet<Order> Orders => Set<Order>();
	public DbSet<OrderItem> OrderItems => Set<OrderItem>();

	public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Pizza>().HasIndex(p => p.Nummer).IsUnique();
		modelBuilder.Entity<Topping>().HasIndex(t => t.Navn).IsUnique();

		modelBuilder.Entity<PizzaTopping>().HasKey(pt => new { pt.PizzaId, pt.ToppingId });

		modelBuilder.Entity<PizzaTopping>()
			.HasOne(pt => pt.Pizza)
			.WithMany(p => p.PizzaToppings)
			.HasForeignKey(pt => pt.PizzaId);

		modelBuilder.Entity<PizzaTopping>()
			.HasOne(pt => pt.Topping)
			.WithMany(t => t.PizzaToppings)
			.HasForeignKey(pt => pt.ToppingId);

		modelBuilder.Entity<Customer>().HasIndex(c => c.Phone).IsUnique();
		modelBuilder.Entity<Customer>().HasQueryFilter(c => c.DeletedAtUtc == null);

		base.OnModelCreating(modelBuilder);
	}
}
