using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aether.Workbench.Abstractions;

/// <summary>活动工作台实例。ContentControl 绑定它（经 DataTemplate → WorkspaceView 渲染其布局树）。</summary>
public interface IWorkspace
{
    string Id { get; }
    string Title { get; }
    IReadOnlyList<IPanel> Panels { get; }
    LayoutNode Layout { get; }

    /// <summary>rail 底部齿轮直达的 panel Key（null = 该工作台不用底部入口，走引擎自带 flyout）。
    /// 默认实现给 null——旧实现者无需改动。</summary>
    string? FooterPanelKey => null;

    /// <summary>当前选中 tab 索引（MVP：单 Tabbed 组）。WorkspaceView 双向同步 TabControl.SelectedIndex。</summary>
    int SelectedTabIndex { get; set; }

    Task InitializeAsync(CancellationToken ct = default);
    Task DeactivateAsync(CancellationToken ct = default);
}
