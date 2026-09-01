using CommunityToolkit.Mvvm.ComponentModel;

namespace MTranslate.Desktop.ViewModels;

public abstract class PageViewModel(string title, string subtitle) : ObservableObject
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public virtual void OnNavigatedTo() { }
}

public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record ModelOption(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}
