using System.Text.Json;
using Aether.Workbench.Abstractions;

namespace Aether.Workbench.Tests;

/// <summary>
/// 布局树是「声明式、可序列化」契约（插件/AI 只需产出 <see cref="WorkspaceDefinition"/>）——
/// 所以 JSON 形状（$kind 判别键、Pixels、Proportional）一旦漂移，用户的布局档就打不开了。
/// 这里钉住多态往返与 FindFirstTabbed 的深度优先语义。
/// </summary>
public class LayoutNodeTests
{
    private static readonly JsonSerializerOptions Json = new();

    [Fact]
    public void 嵌套split里第一个tabbed被深度优先找到()
    {
        var inner = new LayoutTabbed(new[] { "a", "b" }, 1);
        var root = new LayoutSplit(SplitOrientation.Horizontal, new LayoutNode[]
        {
            new LayoutRegion("left"),
            new LayoutSplit(SplitOrientation.Vertical, new LayoutNode[] { new LayoutRegion("mid"), inner }),
            new LayoutTabbed(new[] { "x" }),   // 更靠后——不该被选中
        });

        var found = LayoutNode.FindFirstTabbed(root);

        Assert.Same(inner, found);
        Assert.Equal(1, found!.SelectedIndex);
    }

    [Fact]
    public void 没有tabbed时返回null()
    {
        var root = new LayoutSplit(SplitOrientation.Vertical, new LayoutNode[] { new LayoutRegion("only") });
        Assert.Null(LayoutNode.FindFirstTabbed(root));
        Assert.Null(LayoutNode.FindFirstTabbed(null));
    }

    [Fact]
    public void JSON往返保持三种节点与判别键()
    {
        LayoutNode root = new LayoutSplit(
            SplitOrientation.Horizontal,
            new LayoutNode[]
            {
                new LayoutRegion("sidebar"),
                new LayoutSplit(SplitOrientation.Vertical,
                    new LayoutNode[] { new LayoutTabbed(new[] { "chat", "log" }, 1) },
                    Pixels: new[] { 0d, 420d },
                    Proportional: true),
            },
            Pixels: new[] { 240d, 0d });

        var json = JsonSerializer.Serialize(root, Json);
        Assert.Contains("\"$kind\":\"split\"", json);
        Assert.Contains("\"$kind\":\"region\"", json);
        Assert.Contains("\"$kind\":\"tabbed\"", json);

        var back = Assert.IsType<LayoutSplit>(JsonSerializer.Deserialize<LayoutNode>(json, Json));
        Assert.Equal(SplitOrientation.Horizontal, back.Orientation);
        Assert.Equal(new double[] { 240d, 0d }, back.Pixels);

        var nested = Assert.IsType<LayoutSplit>(back.Children[1]);
        Assert.True(nested.Proportional);                       // 比例分摊标志要活过往返
        Assert.Equal(new double[] { 0d, 420d }, nested.Pixels);
        var tabbed = Assert.IsType<LayoutTabbed>(nested.Children[0]);
        Assert.Equal(new[] { "chat", "log" }, tabbed.RegionIds);
        Assert.Equal(1, tabbed.SelectedIndex);
    }

    [Fact]
    public void 老档无Pixels与Proportional键_反序列化为默认值()
    {
        // 老 JSON（2026-10-05 之前）没有 Pixels/Proportional——必须照样能读（否则升级即丢布局）。
        // 键名与宿主一致：WorkbenchShellViewModel 用**默认** JsonSerializerOptions（PascalCase，无大小写宽容）。
        // 枚举无 JsonStringEnumConverter ⇒ 落盘是数字（Vertical = 1）——这也是老档的真实形状。
        const string legacy = """
            {"$kind":"split","Orientation":1,"Children":[{"$kind":"region","RegionId":"main"}]}
            """;

        var node = Assert.IsType<LayoutSplit>(JsonSerializer.Deserialize<LayoutNode>(legacy, Json));

        Assert.Null(node.Pixels);
        Assert.False(node.Proportional);
        Assert.Equal(SplitOrientation.Vertical, node.Orientation);
        Assert.Equal("main", Assert.IsType<LayoutRegion>(node.Children[0]).RegionId);
    }
}
