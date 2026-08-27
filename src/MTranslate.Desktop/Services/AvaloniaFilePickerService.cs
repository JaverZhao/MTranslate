using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace MTranslate.Desktop.Services;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<IReadOnlyList<string>> PickDocumentsAsync()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window?.StorageProvider is null)
            return [];
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要翻译的文件",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("MTranslate 支持的文档") { Patterns = ["*.txt", "*.srt", "*.vtt", "*.md", "*.markdown", "*.ass"] }
            ]
        });
        return files.Select(file => file.Path.LocalPath).ToArray();
    }

    public Task OpenContainingFolderAsync(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe") { UseShellExecute = true }
            : new ProcessStartInfo("open") { UseShellExecute = false };
        startInfo.ArgumentList.Add(folder);
        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    public async Task<string?> PickOutputFolderAsync()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window?.StorageProvider is null)
            return null;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择翻译输出目录",
            AllowMultiple = false
        });
        return folders.FirstOrDefault()?.Path.LocalPath;
    }
}
