using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MTranslate.Api;
using MTranslate.Desktop.Services;
using MTranslate.Desktop.ViewModels;
using MTranslate.Desktop.Views;

namespace MTranslate.Desktop;

public sealed partial class App : Application
{
    private ServiceProvider? services;
    private bool shutdownInProgress;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            services = ConfigureServices();
            services.GetRequiredService<LocalApiService>().StartAsync().GetAwaiter().GetResult();
            var mainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>()
            };
            mainWindow.Closing += OnMainWindowClosing;
            desktop.MainWindow = mainWindow;
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        collection.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        collection.AddSingleton<DesktopTranslationCoordinator>();
        collection.AddSingleton<ITranslationCoordinator>(services => services.GetRequiredService<DesktopTranslationCoordinator>());
        collection.AddSingleton<ILocalApiTranslationBackend>(services => services.GetRequiredService<DesktopTranslationCoordinator>());
        collection.AddSingleton<LocalApiService>();
        collection.AddSingleton<ILocalApiService>(services => services.GetRequiredService<LocalApiService>());
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

    private async void OnMainWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (shutdownInProgress)
            return;
        shutdownInProgress = true;
        eventArgs.Cancel = true;
        var provider = services;
        services = null;
        if (provider is not null)
            await Task.Run(provider.Dispose);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
