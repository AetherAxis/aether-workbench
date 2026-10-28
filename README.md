# Aether.Workbench

**多 App 布局引擎**——数据驱动的工作台 shell 平台：声明式布局树（`WorkspaceDefinition`，可 JSON 持久化/生成）、
导航 rail（从布局派生）、主题系统（内置 light/dark/dream + JSON overlay 换肤 + aurora 动画层）、
命名 region 注入（插件面板）、跨重启的布局/主题/窗口尺寸持久化。**未来 N 个不同的 App 都基于此引擎构建。**

## 组成

| 目录 | 职责 |
|---|---|
| `Abstractions/` | 布局树（`LayoutNode`：Split/Region/Tabbed，JSON 多态序列化）、`WorkspaceDefinition`、`IViewRegistry`（view-key → 视图/VM 工厂）、`IRegionManager`（命名 region 注入）、`IWorkspace(Manager)` |
| `Workspace/` | 引擎实现：`WorkspaceManager`（激活/生命周期）、`RegionManager`、`ViewRegistry`、`WorkspaceViewModel`、`PanelViewModel` |
| `WorkspaceView.axaml(.cs)` | 布局渲染器：遍历 LayoutNode 树运行时构建视觉树（Grid+GridSplitter / Grid 叠放 tab / ContentControl region）；拖动后布局收集 + 持久化 |
| `WorkbenchWindow.axaml(.cs)` | 精简窗口壳：自绘标题栏 chrome（mac 交通灯/Win 三键/borderless）、nav rail、主题应用、aurora 梦幻背景、窗口尺寸持久化 |
| `WorkbenchShellViewModel.cs` | 干净壳 VM：工作台激活 + nav rail 派生 + `IThemeHost`（主题状态/选择/持久化）+ 布局恢复/重置。**零 App 血统** |
| `Theming/` | 主题引擎：`ThemeLoader`（JSON overlay 叠加 + 内置主题清单）、`ThemeDefinition`、`IThemeHost`、`ThemeOption`、dream 样式 |
| `Themes/` | `WorkbenchTheme.axaml`（设计 token 基线）+ `SharedTheme.axaml`（shell chrome 样式：nav-rail/win-btn/mac-tl/split-*） |
| `Assets/` | 内置主题 JSON（light/dark/dream/dream-dark） |
| `Shell/Codicons.cs` | VSCode codicon path data 集中访问点（nav 图标体系） |
| `IconLoader.cs` | 常用 panel key → codicon 解析目录 |
| `IPreferencesStore.cs` | 持久化抽象（布局/主题/窗口尺寸）——**App 提供实现**（如 JSON 文件存储） |

依赖闭包 = 仅 UI 平台包（Avalonia 12.1.4 / Themes.Fluent / CommunityToolkit.Mvvm / IconPacks.Codicons / MS DI）+ BCL。**零业务类型、零 AetherAxis 仓依赖**（依赖 DAG 最底层）。

## App 接入（四步）

```csharp
// ① 定义工作台（纯数据——布局即一份声明，新 App = 新定义）
var workspace = new WorkspaceDefinition(
    Id: "main", Title: "主工作台",
    Panels: new List<PanelDescriptor>
    {
        new("futures",  "futures",  "期货", "futures"),
        new("backtest", "backtest", "回测", "backtest"),
        new("wallet",   "wallet",   "钱包", "wallet"),
    },
    Layout: new LayoutTabbed(new[] { "futures", "backtest", "wallet" }));

// ② 注册引擎 + 贡献自己的视图
services.AddSingleton<IPreferencesStore, JsonPreferencesStore>();  // App 自备
services.AddWorkbench(workspace);
services.AddSingleton<IViewRegistryContributor, AppViewContributor>();  // 注册 panel 视图/VM 工厂

// ③ App 启动时注入容器（view 层 code-behind 经 WorkbenchServices 解析服务）
var provider = services.BuildServiceProvider();
WorkbenchServices.Attach(provider);

// ④ 起窗口
var shell = provider.GetRequiredService<WorkbenchShellViewModel>();
shell.RestoreThemeSync();   // 窗口 Show 前同步恢复主题（免首帧 flash）
var window = new WorkbenchWindow { DataContext = shell };
window.Show();
await shell.InitializeAsync();
```

Panel key 自定义（App 自己的 `PanelKeys` 目录）——引擎不携带任何业务 panel 键（原 monorepo 版的
chat/documents/sourcecontrol 等 PanelKeys 与 WorkbenchViewRegistry 业务注册已剥离，App 经
`IViewRegistryContributor` 贡献）。

## 纪律（保它配得上"无数个 App"）

1. **引擎边界 = 布局/主题/chrome/持久化机制，不含任何 App 血统**——业务类型（CTP/Ledger/AI）永不入仓
2. **零 AetherAxis 仓依赖**——只许 UI 平台 NuGet + BCL；出现业务包即违规
3. **API 由真实 App 塑形，不加投机功能**——dock/撤销/插件市场这类，第二个 App 真正开口要之前不做。引擎小是美德，保持小
4. **消费模型两段走**：现在本地 ProjectReference（兄弟仓并排约定）；API 稳定后发 NuGet
   （私有 feed + `Aether.*` packageSourceMapping 已就位）——届时工作区外的 App 也能引用

## 引擎版剥离清单（自 monorepo 迁移时——2026-10-20）

- `WorkbenchViewRegistry`（引擎里注册 ChatView 等共享视图）→ App 侧贡献者模式替代
- `DefaultWorkspace`（experts+chat 业务布局）→ App 必传定义
- `PanelKeys`（业务 panel 键目录）→ App 自备
- Shell VM 的 `MainViewModelBase` 继承（账号/聊天/LLM/MCP）→ 干净 ObservableObject
- 窗口的桌宠/聊天联动/Trace/崩溃遥测/Toast 宿主/账号 flyout/三面板 toggle → App 自行叠加
- WorkspaceView 的终端/左栏/聊天折叠块（绑 App 侧服务）→ 引擎布局恒定；折叠需求经 `IRegionManager` 或继承叠加
- `DesktopApp.Services` 静态定位 → 引擎自有 `WorkbenchServices`

## 来历

2026-09 建于 Aether monorepo（`apps/Desktop/Acme.Desktop.Workbench`——Workbench shell + JSON 主题系统）；
2026-10-20 切出引擎核独立成仓（更名 `Aether.Workbench`，按上述剥离清单去 App 血统）。
第一个消费者：`aether-ctp-desktop`（连接器/交易/策略/钱包轻量桌面端；2026-10-28 由 `aether-desktop` 改名）。完整决策记录见工作区根 `MIGRATION.md`。

## 构建与测试

```bash
dotnet build aether-workbench.slnx    # 引擎库
dotnet test  aether-workbench.slnx    # tests/Aether.Workbench.Tests：38 例纯逻辑面
```

测试范围：布局树多态 JSON 往返与 `FindFirstTabbed`、布局档版本信封（新档/旧裸档/未来版本/坏档）、
主题 JSON 解析与内置清单、`ViewRegistry`、`RegionManager`（`RegisterRegion` 不碰 `Content`、
view-key 未注册不改内容）。真实渲染（建面板 / GridSplitter / 换肤）靠 Desktop 实跑验收。
