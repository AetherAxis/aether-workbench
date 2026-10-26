using Aether.Workbench.Abstractions;
using Aether.Workbench.Workspace;

namespace Aether.Workbench.Tests;

/// <summary>
/// rail 底部入口（<see cref="WorkspaceDefinition.FooterPanelKey"/>）契约：
/// ① 配了 footer key 的 panel 不进 nav rail（唯一切换入口改成底部齿轮）；
/// ② 齿轮命令切到该 panel 并把 <see cref="WorkbenchShellViewModel.IsFooterCurrent"/> 置真（底部高亮）；
/// ③ 未配时保持引擎原语义——nav 全量、HasFooterPanel=false（底部仍走自带 flyout）。
/// 纯 VM 逻辑，无窗口/渲染，故可在无头环境钉住。
/// </summary>
public class FooterRailTests
{
    private sealed class MemoryPreferences : IPreferencesStore
    {
        private readonly Dictionary<string, string?> _map = new();
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_map.GetValueOrDefault(key));
        public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
        {
            _map[key] = value;
            return Task.CompletedTask;
        }
    }

    private static WorkspaceDefinition Def(string? footerPanelKey) => new(
        Id: "main",
        Title: "测试工作台",
        Panels: new[]
        {
            new PanelDescriptor("chat", "chat", "聊天", "chat"),
            new PanelDescriptor("agents", "agents", "Agents", "agents"),
            new PanelDescriptor("settings", "settings", "设置", "settings"),
        },
        Layout: new LayoutTabbed(new[] { "chat", "agents", "settings" }),
        FooterPanelKey: footerPanelKey);

    private static async Task<(WorkspaceManager Manager, WorkbenchShellViewModel Shell)> BootAsync(string? footerPanelKey)
    {
        var manager = new WorkspaceManager(new ViewRegistry());   // 无注册视图 → VM 为 null，不影响导航逻辑
        var def = Def(footerPanelKey);
        var shell = new WorkbenchShellViewModel(manager, def, new MemoryPreferences());
        await manager.ActivateAsync(def);
        return (manager, shell);
    }

    [Fact]
    public async Task 配了底部入口_panel不进nav且齿轮直达并高亮()
    {
        var (manager, shell) = await BootAsync("settings");

        Assert.True(shell.HasFooterPanel);
        Assert.Equal("设置", shell.FooterPanelTitle);
        Assert.Equal(new[] { "聊天", "Agents" }, shell.WorkbenchNavItems.Select(i => i.Title));
        Assert.False(shell.IsFooterCurrent);

        shell.OpenFooterPanelCommand.Execute(null);

        Assert.Equal(2, shell.ActiveWorkspace!.SelectedTabIndex);   // settings 在 tabbed 的第 3 项
        Assert.True(shell.IsFooterCurrent);
        Assert.DoesNotContain(shell.WorkbenchNavItems, i => i.IsCurrent);   // nav 无项高亮（当前在底部入口）
        Assert.Same(manager.Active, shell.ActiveWorkspace);
    }

    [Fact]
    public async Task 未配底部入口_导航与高亮保持原语义()
    {
        var (_, shell) = await BootAsync(null);

        Assert.False(shell.HasFooterPanel);
        Assert.Equal("设置", shell.FooterPanelTitle);   // 未配时 tooltip 兜底文案
        Assert.Equal(new[] { "聊天", "Agents", "设置" }, shell.WorkbenchNavItems.Select(i => i.Title));
        Assert.True(shell.WorkbenchNavItems[0].IsCurrent);

        shell.OpenFooterPanelCommand.Execute(null);     // 未配 = 无操作

        Assert.Equal(0, shell.ActiveWorkspace!.SelectedTabIndex);
        Assert.False(shell.IsFooterCurrent);
    }
}
