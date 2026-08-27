using Avalonia.Controls;
using Avalonia.Input;
using MTranslate.Desktop.ViewModels;
namespace MTranslate.Desktop.Views;
public sealed partial class FilesView : UserControl
{
    public FilesView()
    {
        InitializeComponent();
        DragDrop.AddDragOverHandler(DropArea, OnDragOver);
        DragDrop.AddDropHandler(DropArea, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.DragEffects = eventArgs.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs eventArgs)
    {
        var files = eventArgs.DataTransfer.TryGetFiles();
        var paths = files?.Select(item => item.Path.LocalPath).ToArray() ?? [];
        if (DataContext is FilesViewModel viewModel)
            viewModel.AddFiles(paths);
    }
}
