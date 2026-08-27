using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace MTranslate.Desktop.Services;

public sealed class AvaloniaClipboardService : IClipboardService
{
    public Task SetTextAsync(string text)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var clipboard = window is null ? null : TopLevel.GetTopLevel(window)?.Clipboard;
        return clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
    }
}
