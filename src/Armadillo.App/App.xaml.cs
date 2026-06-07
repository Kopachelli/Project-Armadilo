using System.Windows;
using Armadillo.App.ViewModels;
using Armadillo.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Appearance;

namespace Armadillo.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton(_ => Registrator.Create());
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        Services = services.BuildServiceProvider();

        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { (Services as IDisposable)?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
