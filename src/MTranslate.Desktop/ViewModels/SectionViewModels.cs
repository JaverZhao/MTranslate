using CommunityToolkit.Mvvm.Input;
using MTranslate.Desktop.Services;

namespace MTranslate.Desktop.ViewModels;

public sealed class HistoryViewModel() : PageViewModel("翻译历史", "查看、复制和管理本机保存的翻译记录。")
{
    public bool IsEmpty => true;
}

public sealed class ModelsViewModel : PageViewModel
{
    private readonly ITranslationCoordinator coordinator;
    private string standardStatus;

    public ModelsViewModel(ITranslationCoordinator coordinator)
        : base("模型", "管理本机模型、磁盘占用和当前运行状态。")
    {
        this.coordinator = coordinator;
        standardStatus = coordinator.ModelStatus;
        RefreshCommand = new RelayCommand(() => StandardStatus = coordinator.ModelStatus);
    }

    public string StandardStatus { get => standardStatus; private set => SetProperty(ref standardStatus, value); }
    public string FastStatus => "上游运行时兼容性尚未通过";
    public IRelayCommand RefreshCommand { get; }
}

public sealed class ApiViewModel() : PageViewModel("本地 API", "为浏览器扩展、OCR 和自动化工具提供受保护的本机接口。")
{
    public string Status => "未启动";
    public string Endpoint => "127.0.0.1:17891";
    public string Availability => "API Gateway 将在 Phase 5 接入";
}

public sealed class SettingsViewModel : PageViewModel
{
    private readonly ITranslationCoordinator coordinator;
    private bool cacheEnabled;
    private bool historyEnabled = true;
    private string interfaceLanguage = "简体中文";
    private string acceleration = "自动";

    public SettingsViewModel(ITranslationCoordinator coordinator)
        : base("设置", "调整翻译、隐私、外观和运行参数。")
    {
        this.coordinator = coordinator;
        cacheEnabled = coordinator.CacheEnabled;
    }

    public IReadOnlyList<string> InterfaceLanguages { get; } = ["简体中文", "English"];
    public IReadOnlyList<string> AccelerationOptions { get; } = ["自动", "CPU", "GPU"];
    public bool CacheEnabled
    {
        get => cacheEnabled;
        set
        {
            if (!SetProperty(ref cacheEnabled, value)) return;
            coordinator.CacheEnabled = value;
        }
    }
    public bool HistoryEnabled { get => historyEnabled; set => SetProperty(ref historyEnabled, value); }
    public string InterfaceLanguage { get => interfaceLanguage; set => SetProperty(ref interfaceLanguage, value); }
    public string Acceleration { get => acceleration; set => SetProperty(ref acceleration, value); }
}
