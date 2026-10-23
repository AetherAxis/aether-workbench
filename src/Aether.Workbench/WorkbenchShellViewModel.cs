using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aether.Workbench.Abstractions;
using Aether.Workbench.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.Workbench;

/// <summary>
/// 工作台 shell ViewModel（引擎版——零 App 血统：无账号/聊天/AI/终端，那些由各 App 自行叠加）。
/// 职责三件：<see cref="ActiveWorkspace"/>（布局激活）+ 工作台导航 rail（从首个 LayoutTabbed 数据驱动派生，
/// 覆盖各 App 的 panel 差异）+ 主题宿主（<see cref="IThemeHost"/>：状态/选择/持久化）。
/// 布局与主题均持久化到 <see cref="IPreferencesStore"/>（App 提供实现）。
/// </summary>
public partial class WorkbenchShellViewModel : ObservableObject, IThemeHost
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly WorkspaceDefinition _defaultWorkspace;
    private readonly IPreferencesStore _preferences;
    private readonly string? _themesDirectory;
    private INotifyPropertyChanged? _navWorkspace;

    /// <summary>布局持久化键（与 WorkspaceView 共用）。</summary>
    private const string LayoutPrefKey = "workbench.layout";

    /// <summary>主题选择持久化键（存主题名：light/dark/用户文件名）。</summary>
    private const string ThemePrefKey = "ui.theme";

    public WorkbenchShellViewModel(
        IWorkspaceManager workspaceManager,
        WorkspaceDefinition defaultWorkspace,
        IPreferencesStore preferences,
        string? themesDirectory = null)
    {
        _workspaceManager = workspaceManager;
        _defaultWorkspace = defaultWorkspace;
        _preferences = preferences;
        _themesDirectory = themesDirectory;
        _workspaceManager.WorkspaceActivated += OnWorkspaceActivated;
    }

    /// <summary>工作台导航 rail 项（数据驱动：从活动工作台的首个 LayoutTabbed 派生——App 的 panel 差异由工作台定义决定）。</summary>
    public ObservableCollection<NavItem> WorkbenchNavItems { get; } = new();

    /// <summary>活动工作台（Window 主区 ContentControl 绑此 → DataTemplate → WorkspaceView）。</summary>
    [ObservableProperty]
    private IWorkspace? _activeWorkspace;

    /// <summary>工作台是否已激活。窗口中间区按此切「加载占位」vs「真实工作台」——
    /// 首帧窗口 Show 到 ActivateAsync 之间有个短暂空窗，用占位避免裸露空白。</summary>
    public bool HasActiveWorkspace => ActiveWorkspace is not null;

    partial void OnActiveWorkspaceChanged(IWorkspace? value) => OnPropertyChanged(nameof(HasActiveWorkspace));

    /// <summary>可选主题列表（内置 light/dark/dream 系 + &lt;themesDirectory&gt;/*.json）。两 UI 绑此。</summary>
    public ObservableCollection<ThemeOption> Themes { get; } = new();

    // C# 属性无返回类型协变：ObservableCollection 不能隐式实现 IReadOnlyList 接口属性，须显式实现。
    IReadOnlyList<ThemeOption> IThemeHost.Themes => Themes;

    /// <summary>当前主题名（light/dark/用户文件名）。设此属性 + <see cref="CustomThemeJson"/> 触发窗口换肤。</summary>
    [ObservableProperty]
    private string _theme = "light";

    /// <summary>自定义主题 JSON（null = 仅内置 light；非空 = ThemeLoader 叠加 overlay）。</summary>
    [ObservableProperty]
    private string? _customThemeJson;

    /// <summary>当前主题是否深色倾向——驱动 Avalonia 原生控件的 ThemeVariant（light→Light / dark→Dark）。
    /// JSON overlay 只覆盖自定义 token 画刷；原生控件须经 ThemeVariant 翻转。</summary>
    [ObservableProperty]
    private bool _isDarkVariant;

    /// <summary>是否显示动画梦幻背景层（aurora 漂移光斑）。由主题 JSON 的 animated 字段驱动（dream 类皮肤 true）。
    /// WorkbenchWindow 订阅此属性变化启停 aurora；普通主题 false = 静态背景，aurora 停跑省 CPU。</summary>
    [ObservableProperty]
    private bool _isDreamLayerVisible;

    /// <summary>是否无边框自绘标题栏（dream 类皮肤 chrome.borderless=true）。WorkbenchWindow 订阅切换 WindowDecorations。</summary>
    [ObservableProperty]
    private bool _isBorderless;

    /// <summary>自绘标题栏高度（px）。borderless=true 时生效，默认 36。由主题 JSON chrome.titleBarHeight 驱动。</summary>
    [ObservableProperty]
    private double _titleBarHeight = 36;

    /// <summary>主题变化（overlay JSON 或明暗倾向改变）时触发。子窗口（设置等）订阅以即时重染。</summary>
    public event EventHandler? ThemeChanged;

    /// <inheritdoc/>
    bool IThemeHost.IsDark => IsDarkVariant;

    // overlay JSON 或明暗倾向一变即通知子窗口重染。
    partial void OnCustomThemeJsonChanged(string? value) => ThemeChanged?.Invoke(this, EventArgs.Empty);
    partial void OnIsDarkVariantChanged(bool value) => ThemeChanged?.Invoke(this, EventArgs.Empty);
    partial void OnIsBorderlessChanged(bool value) => ThemeChanged?.Invoke(this, EventArgs.Empty);
    partial void OnTitleBarHeightChanged(double value) => ThemeChanged?.Invoke(this, EventArgs.Empty);

    private void OnWorkspaceActivated(object? sender, IWorkspace ws)
    {
        // 解订阅旧工作台的 SelectedTabIndex 变更，订阅新工作台——直接点 tab 时同步 nav 高亮。
        if (_navWorkspace is not null)
            _navWorkspace.PropertyChanged -= OnWorkspaceSelectedTabChanged;
        _navWorkspace = ws as INotifyPropertyChanged;
        if (_navWorkspace is not null)
            _navWorkspace.PropertyChanged += OnWorkspaceSelectedTabChanged;

        ActiveWorkspace = ws;
        RebuildNav(ws);
        UpdateNavCurrent();
    }

    private void OnWorkspaceSelectedTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IWorkspace.SelectedTabIndex))
            UpdateNavCurrent();
    }

    /// <summary>从活动工作台的首个 LayoutTabbed 节点派生导航 rail 项（数据驱动）。
    /// 连续项 <see cref="PanelDescriptor.Group"/> 变化时标记 <see cref="NavItem.StartsGroup"/>——rail 里分组首项上方加间距。</summary>
    private void RebuildNav(IWorkspace ws)
    {
        WorkbenchNavItems.Clear();
        var tabbed = LayoutNode.FindFirstTabbed(ws.Layout);
        if (tabbed is null) return;
        string? prevGroup = null;
        var isFirst = true;
        foreach (var rid in tabbed.RegionIds)
        {
            var panel = ws.Panels.FirstOrDefault(p => p.RegionId == rid);
            if (panel is null) continue;
            // panel.Icon 是 codicon kind key；IconLoader 解析，未命中回退原串（当作直接 path data）。
            var icon = IconLoader.GetPathData(panel.Icon) ?? panel.Icon;
            WorkbenchNavItems.Add(new NavItem
            {
                Key = panel.Key,
                Icon = icon,
                Title = panel.Title,
                Command = SwitchContentCommand,
                StartsGroup = !isFirst && panel.Group != prevGroup,
            });
            prevGroup = panel.Group;
            isFirst = false;
        }
    }

    /// <summary>导航 rail 切内容：映射 panel Key → 工作台 tab 索引（按首个 LayoutTabbed 的 RegionId 顺序）。</summary>
    [RelayCommand]
    private void SwitchContent(string key)
    {
        if (ActiveWorkspace is null) return;
        var tabbed = LayoutNode.FindFirstTabbed(ActiveWorkspace.Layout);
        if (tabbed is null) return;
        for (int i = 0; i < tabbed.RegionIds.Count; i++)
        {
            var panel = ActiveWorkspace.Panels.FirstOrDefault(p => p.RegionId == tabbed.RegionIds[i]);
            if (panel is not null && panel.Key == key)
            {
                ActiveWorkspace.SelectedTabIndex = i;
                break;
            }
        }
        UpdateNavCurrent();
    }

    private void UpdateNavCurrent()
    {
        var idx = ActiveWorkspace?.SelectedTabIndex ?? 0;
        var tabbed = ActiveWorkspace is null ? null : LayoutNode.FindFirstTabbed(ActiveWorkspace.Layout);
        string? currentKey = null;
        if (tabbed is not null && idx >= 0 && idx < tabbed.RegionIds.Count)
        {
            var rid = tabbed.RegionIds[idx];
            currentKey = ActiveWorkspace!.Panels.FirstOrDefault(p => p.RegionId == rid)?.Key;
        }
        foreach (var item in WorkbenchNavItems)
            item.IsCurrent = item.Key == currentKey;
    }

    /// <summary>启动期：恢复主题 + 恢复持久化布局（用户拖动后的 GridSplitter 位置 + tab 选择）后激活工作台。
    /// IWorkspace.Layout 只读——必须在建工作台前把持久化布局合并进 definition，不能改已建工作台。</summary>
    public async Task InitializeAsync()
    {
        await LoadThemesAsync();
        var savedTheme = await _preferences.GetAsync(ThemePrefKey);
        await SelectThemeCoreAsync(savedTheme ?? "light", default);
        var def = await RestoreLayoutAsync(_defaultWorkspace);
        await _workspaceManager.ActivateAsync(def);
    }

    // ── 主题（IThemeHost 实现） ──

    /// <summary>扫描主题列表：内置（ThemeLoader.BuiltInThemes 清单）+ &lt;themesDirectory&gt;/*.json
    /// （用户主题，同名内置不重复——用户放同名文件是覆盖内置而非新增条目）。启动调一次，不热重载。
    /// 每个选项注入 SelectThemeCommand——菜单经 Click code-behind 调 SelectCommand，
    /// 避开 $parent[MenuItem] 跨 popup 的绑定歧义。</summary>
    private Task LoadThemesAsync()
    {
        Themes.Clear();
        // 内置主题从 ThemeLoader.BuiltInThemes 清单驱动（新增内置主题只需在清单加一行）。
        foreach (var (name, desc) in ThemeLoader.BuiltInThemes)
            Themes.Add(new ThemeOption(name, desc, true) { SelectCommand = SelectThemeCommand });
        if (!string.IsNullOrEmpty(_themesDirectory) && Directory.Exists(_themesDirectory))
        {
            foreach (var f in Directory.EnumerateFiles(_themesDirectory, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (ThemeLoader.IsBuiltIn(name)) continue; // 同名文件不覆盖内置
                Themes.Add(new ThemeOption(name, Path.GetFileName(f), false) { SelectCommand = SelectThemeCommand });
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>选中主题：设 overlay JSON + 当前名 + IsCurrent 高亮 + IsDarkVariant + 持久化。供 RelayCommand 与 IThemeHost.SelectThemeAsync 共用。
    /// 内置主题 = 内嵌 Assets/{name}.json 的 JSON overlay；用户可在 &lt;themesDirectory&gt;/{name}.json 放同名文件覆盖内置（免重编）。</summary>
    private async Task SelectThemeCoreAsync(string name, CancellationToken ct)
    {
        // 内置主题：优先用户同名文件（自定义覆盖），缺则内嵌 JSON。用户主题：读文件。
        var json = ThemeLoader.IsBuiltIn(name)
            ? (await ReadUserThemeJsonAsync(name, ct)) ?? ThemeLoader.GetBuiltInJson(name)
            : await ReadUserThemeJsonAsync(name, ct);
        // 用户主题文件缺失/非法 → 回退内置 light（免残留指向不存在的主题）。
        if (json is null)
        {
            name = "light";
            json = ThemeLoader.GetLightJson();
        }
        ApplyThemeState(name, json);   // 设内存态（IsDarkVariant/动画层/chrome/CustomThemeJson/Theme/IsCurrent）
        await _preferences.SetAsync(ThemePrefKey, name, ct);
    }

    /// <summary>★ 启动同步主题恢复：读持久化 ui.theme + 对应 JSON，设全部内存态——在窗口 Show 之前同步调，
    /// 使第一帧即渲染选中主题（免「先 flash 默认 light 再切真主题」）。不写 pref（恢复 ≠ 选中）。</summary>
    public void RestoreThemeSync()
    {
        if (Themes.Count == 0) LoadThemesAsync(); // 确保主题列表已填（IsCurrent 高亮依赖它）
        var name = _preferences.GetAsync(ThemePrefKey).GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(name)) name = "light";
        string? json;
        if (ThemeLoader.IsBuiltIn(name))
        {
            json = ReadUserThemeJsonSync(name) ?? ThemeLoader.GetBuiltInJson(name);
        }
        else
        {
            json = ReadUserThemeJsonSync(name);
        }
        if (json is null) { name = "light"; json = ThemeLoader.GetLightJson(); }
        ApplyThemeState(name, json);
    }

    /// <summary>把「主题名 + JSON」落地为内存态：deserialize → IsDarkVariant/IsDreamLayerVisible/chrome/
    /// CustomThemeJson/Theme/IsCurrent。选中与恢复两路径共用，保证状态一致。</summary>
    private void ApplyThemeState(string name, string? json)
    {
        // 深色倾向：读 JSON variant 字段（"light"→false，缺省/其它→true）。
        var def = ThemeLoader.Deserialize(json ?? "");
        IsDarkVariant = !string.Equals(def?.Variant, "light", StringComparison.OrdinalIgnoreCase);
        IsDreamLayerVisible = def?.Animated == true;   // dream 类皮肤开 aurora 动画背景层
        ApplyChrome(def);   // dream 类皮肤 borderless=true → 无边框自绘标题栏
        CustomThemeJson = json;   // 触发 ThemeLoader.Apply（窗口订阅 CustomThemeJson 变化）
        Theme = name;             // 当前名（高亮）
        foreach (var t in Themes)
            t.IsCurrent = t.Name == name;
    }

    /// <summary>读用户主题 JSON 文件。文件不存在返 null（调用方回退内置）。</summary>
    private async Task<string?> ReadUserThemeJsonAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_themesDirectory)) return null;
        var path = Path.Combine(_themesDirectory, name + ".json");
        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
    }

    /// <summary>同步读用户主题 JSON（启动期 <see cref="RestoreThemeSync"/> 用）。</summary>
    private string? ReadUserThemeJsonSync(string name)
    {
        if (string.IsNullOrEmpty(_themesDirectory)) return null;
        var path = Path.Combine(_themesDirectory, name + ".json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>UI 绑定的选择命令（设置菜单主题子菜单）。</summary>
    [RelayCommand]
    private async Task SelectTheme(string name)
        => await SelectThemeCoreAsync(name ?? "light", default);

    /// <inheritdoc/>
    Task IThemeHost.SelectThemeAsync(string name, CancellationToken cancellationToken)
        => SelectThemeCoreAsync(name, cancellationToken);

    /// <summary>从主题定义读 chrome 字段设 <see cref="IsBorderless"/>/<see cref="TitleBarHeight"/>。
    /// null def / 无 chrome = 系统原生 chrome（borderless=false，高度保持默认）。</summary>
    private void ApplyChrome(ThemeDefinition? def)
    {
        IsBorderless = def?.Chrome?.Borderless == true;
        if (def?.Chrome?.TitleBarHeight is { } h and > 0)
            TitleBarHeight = h;
    }

    // ── 布局持久化 ──

    /// <summary>从偏好存储恢复用户自定义布局。校验区域集与 definition 一致（面板增删则回退默认），反序列化失败也回退（不崩）。</summary>
    private async Task<WorkspaceDefinition> RestoreLayoutAsync(WorkspaceDefinition def)
    {
        try
        {
            var json = await _preferences.GetAsync(LayoutPrefKey);
            if (string.IsNullOrWhiteSpace(json)) return def;
            var layout = LayoutArchiveSerializer.TryDeserialize(json);   // 信封/旧裸档/版本过新都走这里
            if (layout is null || !CoversSameRegions(layout, def.Layout)) return def; // 坏档/未来版本/结构变 → 回退
            return def with { Layout = layout };
        }
        catch
        {
            return def; // 反序列化失败 → 回退（不崩）
        }
    }

    /// <summary>两棵布局树覆盖相同的 region id 序列 + split 旗标（有序比较：面板增删、tab 顺序变更都会不等 → 回退默认）。</summary>
    private static bool CoversSameRegions(LayoutNode a, LayoutNode b)
        => CollectRegions(a).SequenceEqual(CollectRegions(b));

    /// <summary>按遍历顺序收集布局树中所有 region id（LayoutSplit 按 Children 顺序、LayoutTabbed 按 RegionIds 顺序）。</summary>
    private static List<string> CollectRegions(LayoutNode n)
    {
        var list = new List<string>();
        void Walk(LayoutNode x)
        {
            switch (x)
            {
                case LayoutSplit s:
                    // split 旗标 token：Proportional 语义变化时旧存档须回退默认——带进序列参与有序比较。
                    list.Add($"$split:{s.Proportional}");
                    foreach (var c in s.Children) Walk(c);
                    break;
                case LayoutRegion r:
                    list.Add(r.RegionId);
                    break;
                case LayoutTabbed t:
                    foreach (var id in t.RegionIds) list.Add(id);
                    break;
            }
        }
        Walk(n);
        return list;
    }

    /// <summary>清除已保存的布局并重新激活默认工作台（重置 GridSplitter 位置 + tab 选择）。
    /// 重新激活会建新 WorkspaceView；旧 view 的 OnHostClosing 守卫（_workspace != Active）确保关闭时不覆盖重置。</summary>
    [RelayCommand]
    private async Task ResetLayout()
    {
        await _preferences.SetAsync(LayoutPrefKey, null);
        await _workspaceManager.ActivateAsync(_defaultWorkspace);
    }
}
