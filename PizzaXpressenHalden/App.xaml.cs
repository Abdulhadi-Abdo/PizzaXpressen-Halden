using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using PizzaXpressenHalden.ViewModels;

namespace PizzaXpressenHalden;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                var dbPath = Path.Combine(AppContext.BaseDirectory, "PizzaXpressen.db");
                services.AddDbContext<AppDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));

                services.AddSingleton<CsvImportService>();
                services.AddSingleton<CustomerRetentionService>();
                services.AddSingleton<DbInitializer>();
                services.AddSingleton<PrintService>();

                services.AddSingleton<INavigationService, NavigationService>();

                services.AddSingleton<MainViewModel>();
                services.AddTransient<OrderViewModel>();
                services.AddTransient<PizzaRegisterViewModel>();
                services.AddTransient<OrderLogViewModel>();

                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var initializer = _host.Services.GetRequiredService<DbInitializer>();
        var dataFolder = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataFolder);
        await initializer.InitializeAsync(dataFolder);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
