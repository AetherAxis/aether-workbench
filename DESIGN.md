# Aether.Workbench 设计说明

> 本文件写「怎么设计 / 为什么这么设计 / 还能往哪升级」。工作区边界与迁移决策以根 `MIGRATION.md` 为准。

## 1. 定位与边界

多 App 布局引擎：数据驱动桌面前端 shell，含声明式布局树、布局派生 rail、可换肤主题、region 注入与持久化抽象。
依赖仅 Avalonia/Fluent/Mvvm/Codicons/DI，零 AetherAxis 仓依赖（DAG 最底层）。
引擎零业务类型：panel 键/视图/存储均由 App 提供。

## 2. 项目组成

| 目录/文件 | 职责 | 关键类型 | 出向依赖 |
|---|---|---|---|
| `Abstractions/` | 布局树与引擎契约 | `LayoutNode`（Split/Region/Tabbed）、`WorkspaceDefinition`、`IViewRegistry(Contributor)`、`IRegionManager`、`IWorkspace(Manager)` | Json、Avalonia.Controls |
| `Workspace/` | 注册表/region/工作台实现 | `ViewRegistry`、`RegionManager`、`WorkspaceManager`、`WorkspaceViewModel` | CommunityToolkit.Mvvm |
| `WorkspaceView.axaml(.cs)` | 遍历布局树建视觉树；关窗持久化尺寸 | `WorkspaceView` | Avalonia.Controls/Layout |
| `WorkbenchWindow.axaml(.cs)`、`WorkbenchShellViewModel.cs` | 窗口壳与壳 VM（rail/标题栏/aurora/主题/布局） | `WorkbenchWindow`、`WorkbenchShellViewModel` | Theming、`IPreferencesStore` |
| `Theming/`、`Themes/`、`Assets/` | 主题引擎与皮肤资产 | `ThemeLoader`、`ThemeDefinition`、`IThemeHost`、四个内置 JSON | Avalonia.Media/Styling |
| `Shell/`、`IconLoader.cs`、`NavItem.cs`、根级三文件 | 图标/rail 项；存储抽象、定位器、DI 入口 | `IconLoader`、`NavItem`、`IPreferencesStore`、`AddWorkbench` | IconPacks.Codicons、MS DI |

## 3. 关键设计决策

### 3.1 布局树声明式 + JSON 多态序列化
- **决策**：record 三态 + `$kind` 判别符 + 纯数据 `WorkspaceDefinition`。
- **理由**：新 App = 新定义；插件/AI 生成同一对象；`with` 恢复只换 `Layout`。
- **代价**：`Layout` 只读须先恢复后激活；兼容靠 region 序列而非 schema。

### 3.2 导航 rail 从布局派生
- **决策**：`RebuildNav` 取首个 `LayoutTabbed` 的 `RegionIds` 生成 `NavItem`。
- **理由**：布局与导航单一权威源，不会「rail 有项、布局无区」。
- **代价**：仅首个 Tabbed 可导航；视图一次全建常驻。

### 3.3 主题 = 编译期 token 基线 + JSON overlay
- **决策**：基线 .axaml + JSON overlay 经 `ThemeLoader.Apply` merge，全走 `{DynamicResource}`。
- **理由**：换 JSON 即换肤；原生控件另以 `variant` 驱动 `RequestedThemeVariant`。
- **代价**：无版本/字段校验，未知键与非法色静默跳过；不热重载。

### 3.4 App 经 IViewRegistryContributor 贡献视图
- **决策**：`AddWorkbench` 注册空 `ViewRegistry`，建容器时遍历贡献者注入 App 工厂。
- **理由**：业务类型永不入仓（`PanelKeys` 等已剥离）。
- **代价**：注册一次性完成；解析走静态定位器。

### 3.5 region 内容变更单一路径
- **决策**：`RegisterRegion` 只登记宿主、不碰 `Content`；初始与插件注入统一走 `AddToRegion`。
- **理由**：曾因注册阶段清空 `Content` 致 chat 空白。
- **代价**：单 panel/region（last-wins），`UnregisterRegion` 无消费者。

### 3.6 持久化抽象 + best-effort 容错
- **决策**：`IPreferencesStore` 只收 string（`workbench.layout`/`ui.theme`/`ui.winW/H`）；读写包 try/catch。
- **理由**：引擎不假设宿主生命周期；UI 状态丢失不应崩应用。
- **代价**：失败无日志；同步等待只适配同步完成的存储。

## 4. 运行与验证

只产出库，无独立入口，验收靠消费者实跑。

- 构建 `dotnet build aether-workbench.slnx -c Release`（CPM 在 `Directory.Packages.props`）。
- CI：push/PR `main` 与手动触发；ubuntu-latest + dotnet 10.0.x；配私有源凭据后 Release 构建，无 Test 步。
- 接入：`WorkspaceDefinition` → `AddWorkbench` + 贡献者/存储 → `WorkbenchServices.Attach` →
  Show 前 `RestoreThemeSync()`、Show 后 `InitializeAsync()`；手动验布局/主题。

## 5. 升级记录（2026-10-24）

| 项 | 变更前 | 变更后 | 依据 / 验证 |
|---|---|---|---|
| Avalonia | 12.1.1 | 12.1.4 | `git diff` 仅该文件三行；未跑 build/test（按要求） |
| Avalonia.Themes.Fluent | 12.1.1 | 12.1.4 | 同上；CI 下次 push 复核 |
| Microsoft.Extensions.DependencyInjection | 10.0.11 | 10.0.13 | 同上；`AddWorkbench` 未变 |

无源码变更；`bin/Release` 仍为升级前产物。

## 6. 待升级项

| # | 优先级 | 项目 | 现状证据 | 建议 | 代价 / 风险 |
|---|---|---|---|---|---|
| 1 | P1 | 无测试项目（0 项） | 全仓仅一个 csproj，无 `*Tests*`；CI 注释与 MIGRATION ⑩ 已记 | 建测试，钉序列化/回退/主题派生 | 渲染需 headless 夹具 |
| 2 | P1 | 布局 JSON 无 schema/版本 | `RestoreLayoutAsync` 失败即回退；`Pixels` 长度仅注释约定 | 加 `version`；校验长度/tab 索引/region 归属 | 旧档需迁移 |
| 3 | P2 | 主题 overlay 无版本/校验 | 无 version；非法色与未知键静默跳过 | 加 `version` + 报告被忽略键 | 需保留容错 |
| 4 | P2 | 静态服务定位器 | `WorkbenchServices` 一次性 `Attach`，code-behind 全经它取服务 | 改注入，定位器退为兼容层 | 触及全部取服务点 |
| 5 | P2 | 同步阻塞与异步契约不符 | 恢复主题/尺寸/关窗写布局均同步等待 | 补同步取或启动快照接口 | 契约变更影响 App |
| 6 | P2 | 注释漂移 | csproj 注释称 Assets glob，实为显式 `<AvaloniaResource Include="Assets/*.json" />`（README 的 12.1.1 已在本次升级中同步为 12.1.4） | 改注释或改 glob | 纯文档 |
| 7 | P3 | 单 panel/region | 注释「多 panel 留后续」；`UnregisterRegion` 无消费者 | 多 panel 用 `ItemsControl` | 无消费者 |
| 8 | P3 | 未启用编译绑定 | csproj `AvaloniaUseCompiledBindingsByDefault=false` | 补 `x:DataType` 后开启 | 工作量不小 |

## 7. 本轮升级落地（2026-10-24）

- **测试项目**（第 1 项）：新建 `tests/Aether.Workbench.Tests`（23→38 例全绿，纯逻辑面、无窗口无 UI 线程）：布局树 `FindFirstTabbed` 深度优先 + 三节点 JSON 多态往返 + 老档缺键兼容；主题 JSON 解析（完整/未知键/缺省/坏档）与内置清单；`ViewRegistry` 与 `RegionManager` 的两条硬规则（`RegisterRegion` 绝不碰 `Content`、view-key 未注册时 `AddToRegion` 返 false 且不改内容）。
- **布局档版本**（第 2 项）：新增 `LayoutArchive`（`Version` + `Root`）与 `LayoutArchiveSerializer`——**新档写信封、旧裸档照读、版本高于本版返 null（不猜）、坏档返 null**；`WorkspaceView` 保存与 `WorkbenchShellViewModel` 恢复都改走它。tab 选中索引经 `ClampSelectedIndex` 夹到合法区间（坏档里 99/-1 不再打崩渲染）。Pixels 长度不匹配本就被消费端 `childIdx < s.Pixels.Length` 兜住，未再另加校验。
- **CI**：删掉无用的 GitHub Packages 凭据步骤（零兄弟仓依赖），加 Test 步骤。

**未做（有意）**：第 3 项主题 overlay 版本号/被忽略键报告（要保留容错，且需确定报告出口）；第 4 项静态服务定位器改注入（触及全部取服务点）；第 5 项同步阻塞契约（影响 App 启动路径）；第 7 项多 panel/region（无消费者）；第 8 项编译绑定（需补全 `x:DataType`，工作量大）。
