using System.IO;
using System.Windows;
using System.Windows.Threading;
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

        // Never vanish silently: log + surface any unhandled error (mirrors Quiver).
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("Dispatcher", args.Exception);
            args.Handled = true;
            MessageBox.Show($"Something went wrong:\n\n{args.Exception.Message}\n\nDetails were logged. The app will keep running.",
                "Armadillo — error", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash("AppDomain", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) => { LogCrash("Task", args.Exception); args.SetObserved(); };

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(_ => Registrator.Create());
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
            Services = services.BuildServiceProvider();

            ApplicationThemeManager.Apply(ApplicationTheme.Dark);

            Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            LogCrash("Startup", ex);
            MessageBox.Show($"Armadillo failed to start:\n\n{ex.Message}\n\nDetails were written to the log.",
                "Armadillo — startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { (Services as IDisposable)?.Dispose(); } catch { }
        base.OnExit(e);
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Armadillo");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"==== {DateTime.Now:u} [{source}] ====\n{ex}\n\n");
        }
        catch { /* logging must never throw */ }
    }
}
