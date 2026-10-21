using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Aether.Workbench.Abstractions;
using Aether.Workbench.Theming;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Aether.Workbench;

/// <summary>
/// 工作台薄 host（引擎版）：nav rail + 工作台主区 ContentControl + 自绘标题栏 chrome + 主题应用。
/// 不决定布局——布局由 <see cref="WorkspaceView"/> 按 ActiveWorkspace.Layout 构建。
/// 主题：订阅 VM 的 Theme/CustomThemeJson 变化 → <see cref="ThemeLoader.Apply"/> 叠加 JSON overlay；
/// 明暗倾向 → Application 级 ThemeVariant（原生控件翻转）。窗口尺寸持久化到 IPreferencesStore。
/// <para>引擎版剥离（原 monorepo 版的 App 血统，各 App 自行叠加）：桌宠/聊天联动/运行 Trace/
/// 崩溃遥测/Toast 宿主/账号 flyout/三面板 toggle。App 可继承或包装本窗口扩展。</para>
/// </summary>
public partial class WorkbenchWindow : Window
{
    private WorkbenchShellViewModel? _vm;

    /// <summary>aurora 漂移定时器（~30fps UI 线程 tick）。IsDreamLayerVisible 变 true 时建（启 3 颗漂移光斑），
    /// 变 false / 窗口关闭时 Stop（停跑省 CPU，避免隐藏元素上动画空转）。</summary>
    /// <remarks>刻意不用 <c>Animation.RunAsync(TranslateTransform,…)</c>——Avalonia 12.1.1 的 TransformAnimator
    /// 会把 target cast 成 <c>Avalonia.Visual</c>，而 TranslateTransform 是 Animatable 非 Visual → InvalidCastException。
    /// 手动 timer 直赋 <c>tf.X/Y</c> 绕开 animator 选择，可靠可调试。</remarks>
    private DispatcherTimer? _auroraTimer;
    private List<(TranslateTransform Tf, double Drift, double DurX, double DurY)>? _auroraBlobs;
    private long _auroraStartMs;

    /// <summary>最大化前保存的还原帧（Width/Height/Position）。macOS 的 borderless 窗口（WindowDecorations=BorderOnly）
    /// 不干净支持 WindowState.Maximized↔Normal 往返——还原时 OS 不记录最大化前的 frame。故最大化/还原走手动
    /// frame 快照：进最大化前存 <see cref="_restoreFrame"/>，还原时写回。null = 当前处于还原态。</summary>
    private (double Width, double Height, PixelPoint Position)? _restoreFrame;

    public WorkbenchWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        RestoreWindowSize(); // 构造期同步恢复，窗口尚未显示 → 不闪烁
    }

    private static IPreferencesStore? PreferencesStore => WorkbenchServices.GetService<IPreferencesStore>();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as WorkbenchShellViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // 初始主题应用（若有自定义 JSON）。
        ThemeLoader.Apply(this, _vm?.CustomThemeJson);
        ApplyThemeVariant();
        ApplyChrome();   // borderless 皮肤（dream）开自绘标题栏；VM 已据 chrome.borderless 设好 IsBorderless
        ApplyPlatformChromeButtons(); // macOS 显示左上交通灯、非 macOS 显示右上三键
        UpdateMaxIcon(); // 据当前最大化态显最大化/还原图标
        // Avalonia 12.1.1 无 StateChanged 事件；WindowState 是 DirectProperty，
        // 经 AvaloniaObject.PropertyChanged 通用事件过滤 WindowStateProperty 捕获最大化↔还原（覆盖按钮/双击/OS snap 全路径）。
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                UpdateMaxIcon();
        };
        // dream 类皮肤开 aurora 动画背景层（VM 在窗口打开前已据主题 animated 字段设好 IsDreamLayerVisible）。
        if (_vm?.IsDreamLayerVisible == true) StartAurora();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        StopAurora();      // 取消漂移动画（免关窗后空转）
        PersistWindowSize();
    }

    /// <summary>设置菜单「主题」子项点击：MenuItem.Click 从叶子子项冒泡到父「主题」，
    /// <c>e.Source</c> 的 DataContext 是被点的 ThemeOption。父项自身 DataContext 是 shell VM（非 ThemeOption）
    /// → 自动跳过。经 ThemeOption.SelectCommand 触发换肤（避开 MenuItem 容器 Command 绑定的跨 popup 歧义）。</summary>
    public void OnSelectTheme(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem mi && mi.DataContext is ThemeOption t)
            t.SelectCommand?.Execute(t.Name);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkbenchShellViewModel.Theme)
            or nameof(WorkbenchShellViewModel.CustomThemeJson))
        {
            ThemeLoader.Apply(this, _vm?.CustomThemeJson);
        }
        if (e.PropertyName == nameof(WorkbenchShellViewModel.IsDarkVariant))
            ApplyThemeVariant();
        if (e.PropertyName == nameof(WorkbenchShellViewModel.IsDreamLayerVisible))
        {
            // 切到/切出 dream 类皮肤：启停 aurora 漂移光斑（隐藏即停跑省 CPU）。
            if (_vm?.IsDreamLayerVisible == true) StartAurora(); else StopAurora();
        }
        if (e.PropertyName is nameof(WorkbenchShellViewModel.IsBorderless)
            or nameof(WorkbenchShellViewModel.TitleBarHeight))
        {
            // 切到/切出 borderless 皮肤（dream）或改标题栏高度 → 重应用 chrome。
            ApplyChrome();
        }
    }

    // —— 自绘标题栏 / 无边框 chrome（dream 类皮肤 borderless=true 启用）——

    /// <summary>按 VM 的 IsBorderless 切窗口 chrome。borderless 皮肤（dream）→
    /// <see cref="global::Avalonia.Controls.WindowDecorations.BorderOnly"/>：去系统标题栏（自绘 CustomTitleBar
    /// 接管 min/max/close + 拖拽 + 双击最大化）但保留边框缩放（免丢 resize）。
    /// 系统原生皮肤（light/dark）→ <see cref="global::Avalonia.Controls.WindowDecorations.Full"/>（OS 标题栏/交通灯）。</summary>
    private void ApplyChrome()
    {
        var borderless = _vm?.IsBorderless ?? false;
        WindowDecorations = borderless ? global::Avalonia.Controls.WindowDecorations.BorderOnly
                                       : global::Avalonia.Controls.WindowDecorations.Full;
    }

    /// <summary>标题栏拖拽：左键按下 BeginMoveDrag（OS 接管移动）。点中按钮时不拖（按钮自处理 Pressed）。</summary>
    public void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;            // 点中窗口按钮 → 让按钮处理，不拖
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);                       // 异步 OS 移动循环；不当 await
    }

    /// <summary>标题栏双击：最大化/还原切换（Windows/桌面惯例）。</summary>
    public void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Button) return;
        ToggleMaximizeRestore();
    }

    /// <summary>最小化（自绘标题栏按钮/交通灯黄点）：普通最小化（引擎版无桌宠驻留语义——App 需要时自行拦截）。</summary>
    public void OnMinimize(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    public void OnMaximizeRestore(object? sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    public void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>最大化/还原切换。borderless 皮肤在 macOS上用 WindowState.Maximized 走 OS 原生最大化→还原时，
    /// OS 不记录最大化前的 frame，导致还原后窗口尺寸/布局错乱。故改手动 frame 快照：进最大化前存
    /// Width/Height/Position，还原时写回，最大化的目标帧取当前屏幕的 WorkingArea（等价 OS 最大化，但完全自控）。
    /// 系统原生 chrome（borderless=false）仍走 WindowState（OS 标题栏自己管 frame，往返正常）。</summary>
    private void ToggleMaximizeRestore()
    {
        if (_vm?.IsBorderless != true)
        {
            // 原生 chrome：OS 自己管最大化 frame，走 WindowState 即可。
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (_restoreFrame is null)
        {
            // —— 进入最大化：快照当前 frame，再铺满当前屏幕 WorkingArea ——
            _restoreFrame = (Width, Height, Position);
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                var wa = screen.WorkingArea;
                Position = wa.Position;
                Width = wa.Width;
                Height = wa.Height;
            }
        }
        else
        {
            // —— 还原：写回快照 frame ——
            var f = _restoreFrame.Value;
            Width = f.Width;
            Height = f.Height;
            Position = f.Position;
            _restoreFrame = null;
        }
        UpdateMaxIcon();
    }

    /// <summary>据当前是否最大化显隐最大化/还原图标（ChromeMaximize / ChromeRestore 互斥）。
    /// borderless 皮肤手动管 frame（不碰 WindowState），最大化态由 <see cref="_restoreFrame"/> 非空判定；
    /// 原生 chrome 仍读 WindowState。</summary>
    private void UpdateMaxIcon()
    {
        var maximized = IsMaximized;
        if (MaxIcon is not null) MaxIcon.IsVisible = !maximized;
        if (RestoreIcon is not null) RestoreIcon.IsVisible = maximized;
        ApplyMacMaxGlyph();
    }

    /// <summary>当前是否处于最大化态。</summary>
    private bool IsMaximized => _vm?.IsBorderless == true ? _restoreFrame.HasValue : WindowState == WindowState.Maximized;

    /// <summary>mac 交通灯绿点（最大化）hover 时显 ⤢（最大化）或 ⤡（还原）。仅 macOS 生效。</summary>
    private void ApplyMacMaxGlyph()
    {
        if (MaxGlyph is null) return;
        var maximized = IsMaximized;
        MaxGlyph.Data = Geometry.Parse(maximized ? Codicons.ChromeRestore : Codicons.ChromeMaximize);
    }

    /// <summary>按平台切换标题栏按钮组：macOS 显示左上交通灯（红黄绿圆点），非 macOS 显示右上 Windows 式三键。</summary>
    private void ApplyPlatformChromeButtons()
    {
        var isMac = OperatingSystem.IsMacOS();
        if (MacTrafficLights is not null) MacTrafficLights.IsVisible = isMac;
        if (WinButtons is not null) WinButtons.IsVisible = !isMac;
        ApplyMacMaxGlyph();
    }

    /// <summary>把主题明暗倾向同步到 Avalonia 原生控件主题（Application 级——所有子窗口一并翻转）。
    /// JSON overlay 只覆盖自定义 token 画刷；原生控件须经 ThemeVariant 翻转。</summary>
    private void ApplyThemeVariant()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = (_vm?.IsDarkVariant ?? false) ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    // —— 窗口尺寸持久化（布局/分隔条由 WorkspaceView + WorkbenchShellViewModel 跨重启持久） ——

    /// <summary>构造期同步恢复窗口尺寸（App 的 preferences 实现若为 JSON 文件存储，GetAsync 同步完成）。</summary>
    private void RestoreWindowSize()
    {
        var store = PreferencesStore;
        if (store is null) return;
        try
        {
            if (double.TryParse(store.GetAsync("ui.winW").GetAwaiter().GetResult(), out var w) && w >= 900)
                Width = w;
            if (double.TryParse(store.GetAsync("ui.winH").GetAwaiter().GetResult(), out var h) && h >= 600)
                Height = h;
        }
        catch { /* 读取失败用默认尺寸 */ }
    }

    /// <summary>关闭时持久化窗口尺寸。fire-and-forget 异步写（不阻塞 UI 线程）。窗口尺寸属低价值数据，
    /// 极端情况丢写仅是下次用默认尺寸。</summary>
    private void PersistWindowSize()
    {
        var store = PreferencesStore;
        if (store is null) return;
        double winW = Width, winH = Height;
        try
        {
            _ = Task.Run(async () =>
            {
                await store.SetAsync("ui.winW", winW.ToString("0.#"));
                await store.SetAsync("ui.winH", winH.ToString("0.#"));
            });
        }
        catch { /* 关闭期写入失败忽略 */ }
    }

    // —— 动画梦幻背景层（aurora 漂移光斑；dream 类皮肤 animated=true 启用）——

    /// <summary>启 aurora：建 3 颗径向渐变光斑（紫/蓝/品红），居中 + TranslateTransform 漂移。
    /// X/Y 周期互质 → 轨迹不重复（有机感）。timer ~30fps 直赋 tf.X/Y 漂移；StopAurora/关窗即停。</summary>
    private void StartAurora()
    {
        if (_auroraTimer is not null) return;       // 已运行（重复触发幂等）
        var layer = DreamLayer;
        if (layer is null) return;

        // 光斑：(颜色 含 alpha, 直径, 漂移幅度 px, X 周期 s, Y 周期 s)
        var specs = new (Color Color, double Size, double Drift, double DurX, double DurY)[]
        {
            (Color.FromArgb(0x55, 0xa7, 0x8b, 0xfa), 680, 150, 24, 31), // 紫
            (Color.FromArgb(0x44, 0x60, 0xa5, 0xfa), 600, 130, 29, 37), // 蓝
            (Color.FromArgb(0x33, 0xd9, 0x46, 0xef), 560, 110, 26, 33), // 品红
        };
        _auroraBlobs = new List<(TranslateTransform, double, double, double)>(specs.Length);
        foreach (var (color, size, drift, durX, durY) in specs)
        {
            var tf = new TranslateTransform();
            var blob = new Ellipse
            {
                Width = size, Height = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = MakeRadialGlow(color),
                RenderTransform = tf,
            };
            layer.Children.Add(blob);
            _auroraBlobs.Add((tf, drift, durX, durY));
        }
        _auroraStartMs = Environment.TickCount64;
        _auroraTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30fps
        _auroraTimer.Tick += OnAuroraTick;
        _auroraTimer.Start();
    }

    /// <summary>aurora tick：按已运行秒数算各光斑 X/Y 正弦漂移，直赋 TranslateTransform。</summary>
    private void OnAuroraTick(object? sender, EventArgs e)
    {
        if (_auroraBlobs is null) return;
        var t = (Environment.TickCount64 - _auroraStartMs) / 1000.0;
        const double twoPi = 2.0 * Math.PI;
        foreach (var (tf, drift, durX, durY) in _auroraBlobs)
        {
            tf.X = Math.Sin(t * twoPi / durX) * drift;
            tf.Y = Math.Sin(t * twoPi / durY) * drift * 0.7;
        }
    }

    /// <summary>停 aurora：Stop timer + 清空 DreamLayer 子控件。</summary>
    private void StopAurora()
    {
        if (_auroraTimer is null) return;
        _auroraTimer.Stop();
        _auroraTimer.Tick -= OnAuroraTick;
        _auroraTimer = null;
        _auroraBlobs = null;
        DreamLayer?.Children.Clear();
    }

    /// <summary>径向渐变光斑画刷：中心 = 有色 alpha → 边缘透明。软发光，免 BlurEffect。</summary>
    private static IBrush MakeRadialGlow(Color c)
    {
        var rg = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
        };
        rg.GradientStops.Add(new GradientStop { Color = c, Offset = 0 });
        rg.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, c.R, c.G, c.B), Offset = 1 });
        return rg;
    }
}
