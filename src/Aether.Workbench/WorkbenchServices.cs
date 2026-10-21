using Microsoft.Extensions.DependencyInjection;

namespace Aether.Workbench;

/// <summary>
/// 引擎级服务定位器：App 启动时 <see cref="Attach"/> 注入容器，view 层（WorkspaceView / WorkbenchWindow
/// 等 code-behind）经此解析 <see cref="IViewRegistry"/> / <see cref="IRegionManager"/> /
/// <see cref="IPreferencesStore"/>——引擎不依赖任何具体 App 的静态类（原 monorepo 版挂靠 DesktopApp）。
/// <para>容错解析：容器未注入/已释放（App 退出时容器先于窗口释放）返 null——布局持久化等
/// best-effort 路径静默跳过，不抛 <c>ObjectDisposedException</c>。</para>
/// </summary>
public static class WorkbenchServices
{
    private static IServiceProvider? _provider;

    /// <summary>App 启动时注入服务容器（一次性）。</summary>
    public static void Attach(IServiceProvider provider) => _provider = provider;

    /// <summary>容错解析（容器未注入/已释放返 null）。</summary>
    public static T? GetService<T>() where T : class
    {
        try { return _provider?.GetService<T>(); }
        catch { return null; }
    }
}
