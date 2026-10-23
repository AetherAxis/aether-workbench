using Aether.Workbench.Abstractions;

namespace Aether.Workbench.Tests;

/// <summary>
/// 布局档的版本信封契约：**新档写信封、旧裸档照读、未来版本不猜（回退默认）、坏档不抛**。
/// 升级不清用户布局是硬要求——用户不会因为换了个版本就愿意重排面板。
/// </summary>
public class LayoutArchiveTests
{
    private static LayoutNode Sample() => new LayoutSplit(
        SplitOrientation.Horizontal,
        new LayoutNode[] { new LayoutRegion("sidebar"), new LayoutTabbed(new[] { "chat", "log" }, 1) },
        Pixels: new[] { 240d, 0d });

    [Fact]
    public void 序列化带版本信封_往返等价()
    {
        var json = LayoutArchiveSerializer.Serialize(Sample());

        Assert.Contains($"\"Version\":{LayoutArchive.CurrentVersion}", json);
        Assert.Contains("\"Root\"", json);

        var back = Assert.IsType<LayoutSplit>(LayoutArchiveSerializer.TryDeserialize(json));
        Assert.Equal(new double[] { 240d, 0d }, back.Pixels);
        Assert.Equal(1, Assert.IsType<LayoutTabbed>(back.Children[1]).SelectedIndex);
    }

    [Fact]
    public void 旧版裸布局树仍可读_不算坏档()
    {
        // 2026-10 之前的档 = 没有信封的裸 LayoutNode（键 PascalCase、枚举为数字）
        const string legacy = """
            {"$kind":"split","Orientation":0,"Children":[{"$kind":"region","RegionId":"main"}],"Pixels":[300.0,0.0]}
            """;

        var node = Assert.IsType<LayoutSplit>(LayoutArchiveSerializer.TryDeserialize(legacy));

        Assert.Equal(SplitOrientation.Horizontal, node.Orientation);
        Assert.Equal("main", Assert.IsType<LayoutRegion>(node.Children[0]).RegionId);
        Assert.Equal(new double[] { 300d, 0d }, node.Pixels);
    }

    [Fact]
    public void 未来版本号_不接受_回退默认()
    {
        var json = """{"Version":""" + (LayoutArchive.CurrentVersion + 1)
            + """ ,"Root":{"$kind":"region","RegionId":"future"}}""".Replace(" ,", ",");

        Assert.Null(LayoutArchiveSerializer.TryDeserialize(json));   // 不认识就不猜
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ 坏")]
    [InlineData("[]")]
    [InlineData("""{"Version":1}""")]                                     // 有版本无 Root
    [InlineData("""{"$kind":"nope","Whatever":1}""")]                     // 未知节点类型
    public void 无档或坏档一律返回null不抛(string? json)
        => Assert.Null(LayoutArchiveSerializer.TryDeserialize(json));

    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(2, 3, 2)]
    [InlineData(99, 3, 2)]     // 越界上夹
    [InlineData(-5, 3, 0)]     // 越界下夹
    [InlineData(7, 0, 0)]      // 无 tab
    public void 选中索引夹到合法区间(int index, int count, int expected)
        => Assert.Equal(expected, LayoutArchiveSerializer.ClampSelectedIndex(index, count));
}
