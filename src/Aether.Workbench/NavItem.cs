using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Workbench;

/// <summary>
/// 导航 rail 项（数据驱动：从活动工作台的首个 LayoutTabbed 派生，见 WorkbenchShellViewModel.RebuildNav）。
/// <see cref="Icon"/> 是 codicon path data（IconLoader 解析 key 得来）；<see cref="StartsGroup"/> 让
/// rail 里分组首项上方加间距（呈现层 Classes.gap 消费）。
/// </summary>
public partial class NavItem : ObservableObject
{
    /// <summary>panel key（点击时作为 SwitchContentCommand 的参数）。</summary>
    public string Key { get; init; } = "";

    /// <summary>codicon path data（IconLoader.GetPathData(panel.Icon) 的解析结果）。</summary>
    public string Icon { get; init; } = "";

    /// <summary>常显标签（rail 图标下方的短标题）。</summary>
    public string Title { get; init; } = "";

    /// <summary>切换命令（指向 shell VM 的 SwitchContentCommand）。</summary>
    public IRelayCommand? Command { get; init; }

    /// <summary>分组首项（连续项 Group 变化时标记——rail 加间距分隔）。</summary>
    public bool StartsGroup { get; init; }

    /// <summary>是否当前选中（高亮/禁用再点）。UpdateNavCurrent 维护。</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
