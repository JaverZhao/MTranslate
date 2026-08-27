using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MTranslate.Desktop.Services;
using MTranslate.Desktop.ViewModels;
using MTranslate.Desktop.Views;

namespace MTranslate.Desktop;

public sealed partial class App : Application
{
    private ServiceProvider? services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            services = ConfigureServices();
            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>()
            };
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        collection.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        collection.AddSingleton<ITranslationCoordinator, DesktopTranslationCoordinator>();
        collection.AddSingleton<HomeViewModel>();
        collection.AddSingleton<FilesViewModel>();
        collection.AddSingleton<HistoryViewModel>();
        collection.AddSingleton<ModelsViewModel>();
        collection.AddSingleton<ApiViewModel>();
        collection.AddSingleton<SettingsViewModel>();
        collection.AddSingleton<MainWindowViewModel>();
        return collection.BuildServiceProvider();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        services?.Dispose();
        services = null;
    }
}
