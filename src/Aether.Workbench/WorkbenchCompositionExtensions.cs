using Aether.Workbench.Abstractions;
using Aether.Workbench.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Aether.Workbench;

/// <summary>
/// 引擎 DI 入口：注册布局引擎（view registry / region manager / workspace manager）+ 默认工作台定义。
/// <para><b>视图注册走贡献者</b>：引擎注册的是空 <see cref="ViewRegistry"/> 基座，App 把自己的
/// panel 视图/VM 工厂注册为 <see cref="IViewRegistryContributor"/>（DI 多例）——引擎不认识任何业务视图
/// （原 monorepo 版的 WorkbenchViewRegistry 在引擎里注册 ChatView 等共享视图，是 App 血统，不迁）。</para>
/// <para>App 在 <c>AddWorkbench</c> 之后 <see cref="WorkbenchServices.Attach"/> 注入容器
/// （view 层 code-behind 经它解析服务）。</para>
/// </summary>
public static class WorkbenchCompositionExtensions
{
    /// <param name="defaultWorkspace">App 的默认工作台定义（必传——引擎不携带任何业务布局）。</param>
    public static IServiceCollection AddWorkbench(this IServiceCollection services, WorkspaceDefinition defaultWorkspace)
    {
        services.AddSingleton<IViewRegistry>(sp =>
        {
            var registry = new ViewRegistry();
            foreach (var contributor in sp.GetServices<IViewRegistryContributor>())
                contributor.Contribute(registry, sp);
            return registry;
        });
        services.AddSingleton<IRegionManager, RegionManager>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton(defaultWorkspace);
        // 壳 VM（引擎机制件——App 起窗口需要它；构造依赖 IWorkspaceManager/WorkspaceDefinition +
        // IPreferencesStore（App 注册）+ 可选 themesDirectory 缺省 null）
        services.AddSingleton<WorkbenchShellViewModel>();
        return services;
    }
}
