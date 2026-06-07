using Armadillo.App.ViewModels;
using Wpf.Ui.Controls;

namespace Armadillo.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.RefreshCommand.ExecuteAsync(null);
    }
}
