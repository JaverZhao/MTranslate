using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;

namespace MTranslate.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private NavigationItemViewModel selectedNavigation;
    private PageViewModel currentPage;

    public MainWindowViewModel(
        HomeViewModel home,
        FilesViewModel files,
        HistoryViewModel history,
        ModelsViewModel models,
        ApiViewModel api,
        SettingsViewModel settings)
    {
        Navigation =
        [
            new("翻译", Icon.Translate, home, Select),
            new("文件", Icon.Document, files, Select),
            new("历史", Icon.History, history, Select),
            new("模型", Icon.BrainCircuit, models, Select),
            new("本地 API", Icon.PlugConnected, api, Select),
            new("设置", Icon.Settings, settings, Select)
        ];
        selectedNavigation = Navigation[0];
        selectedNavigation.IsSelected = true;
        currentPage = home;
    }

    public IReadOnlyList<NavigationItemViewModel> Navigation { get; }
    public PageViewModel CurrentPage { get => currentPage; private set => SetProperty(ref currentPage, value); }
    public NavigationItemViewModel SelectedNavigation { get => selectedNavigation; private set => SetProperty(ref selectedNavigation, value); }

    private void Select(NavigationItemViewModel item)
    {
        if (ReferenceEquals(item, SelectedNavigation)) return;
        SelectedNavigation.IsSelected = false;
        SelectedNavigation = item;
        SelectedNavigation.IsSelected = true;
        CurrentPage = item.Page;
        CurrentPage.OnNavigatedTo();
    }
}

public sealed class NavigationItemViewModel : ObservableObject
{
    private bool isSelected;

    public NavigationItemViewModel(
        string title,
        Icon icon,
        PageViewModel page,
        Action<NavigationItemViewModel> select)
    {
        Title = title;
        Icon = icon;
        Page = page;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Title { get; }
    public Icon Icon { get; }
    public PageViewModel Page { get; }
    public IRelayCommand SelectCommand { get; }
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
}
