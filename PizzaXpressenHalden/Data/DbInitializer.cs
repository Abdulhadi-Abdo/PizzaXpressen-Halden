using System.IO;
using System.Threading.Tasks;

namespace PizzaXpressenHalden;

public class DbInitializer
{
	private readonly AppDbContext _db;
	private readonly CsvImportService _import;

	public DbInitializer(AppDbContext db, CsvImportService import)
	{
		_db = db;
		_import = import;
	}

	public async Task InitializeAsync(string dataFolder)
	{
		await _db.Database.EnsureCreatedAsync();

		await _import.ImportMenuIfEmptyAsync(
			Path.Combine(dataFolder, "pizzaer.csv"),
			Path.Combine(dataFolder, "pizza_toppinger.csv"),
			Path.Combine(dataFolder, "pizza_topping_relasjoner.csv"),
			Path.Combine(dataFolder, "drikker.csv"),
			Path.Combine(dataFolder, "pizza_extras.csv"),
			Path.Combine(dataFolder, "tilbehor.csv")
		);
	}
}
