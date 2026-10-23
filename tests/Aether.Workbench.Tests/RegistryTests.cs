using Avalonia.Controls;
using Aether.Workbench.Abstractions;
using Aether.Workbench.Workspace;

namespace Aether.Workbench.Tests;

/// <summary>
/// 视图注册表与区域管理器的契约。区域管理的两条硬规则来自真实故障：
/// ① <c>RegisterRegion</c> 绝不能碰 <c>host.Content</c>（曾清空宿主把 BuildRegion 已设的视图抹掉 ⇒ chat 空白）；
/// ② view-key 未注册时 <c>AddToRegion</c> 返回 false 且**不改** Content（不把已有内容换成 null）。
/// 这些都不需要窗口/渲染，用裸 <see cref="ContentControl"/> 即可钉住。
/// </summary>
public class RegistryTests
{
    private static PanelViewModel Panel(string regionId, string key = "k", object? vm = null)
        => new(regionId, key, "标题", "icon", vm);

    [Fact]
    public void ViewRegistry_未注册key返回null_已注册按工厂造()
    {
        var registry = new ViewRegistry();
        Assert.Null(registry.CreateView("missing"));
        Assert.Null(registry.CreateViewModel("missing"));

        var vm = new object();
        registry.Register("chat",
            viewFactory: () => new Border { Name = "chat-view" },
            vmFactory: () => vm);

        var view = registry.CreateView("chat");
        Assert.Equal("chat-view", view!.Name);
        Assert.Same(vm, registry.CreateViewModel("chat"));
    }

    [Fact]
    public void ViewRegistry_同key重复注册以后者为准()
    {
        var registry = new ViewRegistry();
        registry.Register("k", () => new Border { Name = "first" });
        registry.Register("k", () => new Border { Name = "second" });
        Assert.Equal("second", registry.CreateView("k")!.Name);
    }

    [Fact]
    public void ViewRegistry_无vm工厂时视图模型为null()
    {
        var registry = new ViewRegistry();
        registry.Register("k", () => new Border());
        Assert.Null(registry.CreateViewModel("k"));
    }

    [Fact]
    public void RegisterRegion只登记不碰Content()
    {
        var regions = new RegionManager(new ViewRegistry());
        var existing = new Border { Name = "preexisting" };
        var host = new ContentControl { Content = existing };   // 宿主已带内容（BuildRegion 设过）

        regions.RegisterRegion("chat", host);

        Assert.Same(existing, host.Content);                   // 绝不被清空
        Assert.Equal(new[] { "chat" }, regions.GetRegionNames());
    }

    [Fact]
    public void AddToRegion_设Content并绑DataContext()
    {
        var registry = new ViewRegistry();
        registry.Register("chat", () => new Border { Name = "chat-view" });
        var regions = new RegionManager(registry);
        var host = new ContentControl();
        regions.RegisterRegion("chat", host);
        var vm = new object();
        var panel = Panel("chat", "chat", vm);

        Assert.True(regions.AddToRegion("chat", panel));
        Assert.Equal("chat-view", Assert.IsType<Border>(host.Content).Name);
        Assert.Same(vm, ((Control)host.Content!).DataContext);
    }

    [Fact]
    public void AddToRegion_未登记region或未注册key_返回false且不改内容()
    {
        var registry = new ViewRegistry();
        var regions = new RegionManager(registry);
        var host = new ContentControl { Content = new Border { Name = "keep" } };
        regions.RegisterRegion("chat", host);

        Assert.False(regions.AddToRegion("nope", Panel("nope")));          // region 未登记
        Assert.False(regions.AddToRegion("chat", Panel("chat", "unreg"))); // view-key 未注册
        Assert.Equal("keep", Assert.IsType<Border>(host.Content).Name);    // 内容原样
    }

    [Fact]
    public void AddToRegion_后到者覆盖_MVP单panel语义()
    {
        var registry = new ViewRegistry();
        registry.Register("a", () => new Border { Name = "a-view" });
        registry.Register("b", () => new Border { Name = "b-view" });
        var regions = new RegionManager(registry);
        var host = new ContentControl();
        regions.RegisterRegion("main", host);

        regions.AddToRegion("main", Panel("main", "a"));
        regions.AddToRegion("main", Panel("main", "b"));

        Assert.Equal("b-view", Assert.IsType<Border>(host.Content).Name);   // last wins
    }

    [Fact]
    public void RemoveFromRegion清内容但保留region_Unregister两者都拆()
    {
        var registry = new ViewRegistry();
        registry.Register("k", () => new Border());
        var regions = new RegionManager(registry);
        var host = new ContentControl();
        regions.RegisterRegion("main", host);
        regions.AddToRegion("main", Panel("main"));

        regions.RemoveFromRegion("main");
        Assert.Null(host.Content);
        Assert.Equal(new[] { "main" }, regions.GetRegionNames());          // region 还在

        regions.AddToRegion("main", Panel("main"));
        regions.UnregisterRegion("main");
        Assert.Null(host.Content);
        Assert.Empty(regions.GetRegionNames());
    }
}
