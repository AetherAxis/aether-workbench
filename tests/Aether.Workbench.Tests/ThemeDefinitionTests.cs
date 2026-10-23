using Aether.Workbench.Theming;

namespace Aether.Workbench.Tests;

/// <summary>
/// 主题 JSON 的用户面：用户可以往 <c>&lt;tenantDir&gt;/themes/*.json</c> 丢自定义主题、也可同名覆盖内置。
/// 所以「坏 JSON 不抛、未知键不炸、缺省有确定语义」是契约；这里钉住解析与内置清单。
/// （把 token 装进 ResourceDictionary 那一步要 Avalonia 平台服务，归 Desktop 实跑验收。）
/// </summary>
public class ThemeDefinitionTests
{
    [Fact]
    public void 完整主题JSON被解析()
    {
        const string json = """
            {
              "colors": { "AccentBrush": "#22C07A", "TextPrimaryColor": "#101820" },
              "fonts": { "MonoFont": "MiSans Mono" },
              "radius": { "CornerRadiusMd": "8" },
              "spacing": { "SpaceMd": "12" },
              "variant": "light",
              "animated": true,
              "chrome": { "borderless": true }
            }
            """;

        var def = ThemeLoader.Deserialize(json);

        Assert.NotNull(def);
        Assert.Equal("#22C07A", def.Colors["AccentBrush"]);
        Assert.Equal("MiSans Mono", def.Fonts["MonoFont"]);
        Assert.Equal("8", def.Radius["CornerRadiusMd"]);
        Assert.Equal("12", def.Spacing["SpaceMd"]);
        Assert.Equal("light", def.Variant);
        Assert.True(def.Animated);
        Assert.True(def.Chrome?.Borderless);
    }

    [Fact]
    public void 未知键被忽略而不是报错()
    {
        var def = ThemeLoader.Deserialize("""{"colors":{"A":"#fff"},"futureKey":{"whatever":1}}""");
        Assert.NotNull(def);
        Assert.Equal("#fff", def!.Colors["A"]);
    }

    [Fact]
    public void 缺省段为空集合而不是null()
    {
        var def = ThemeLoader.Deserialize("{}");
        Assert.NotNull(def);
        Assert.Empty(def!.Colors);
        Assert.Empty(def.Fonts);
        Assert.Empty(def.Radius);
        Assert.Empty(def.Spacing);
        Assert.Null(def.Gradients);
        Assert.Null(def.Background);
        Assert.Null(def.Variant);
    }

    [Fact]
    public void 坏JSON返回null不抛()
    {
        Assert.Null(ThemeLoader.Deserialize("{ 坏"));
        Assert.Null(ThemeLoader.Deserialize(""));
    }

    [Theory]
    [InlineData("light", true)]
    [InlineData("dark", true)]
    [InlineData("dream", true)]
    [InlineData("dream-dark", true)]
    [InlineData("no-such-theme", false)]
    [InlineData("", false)]
    public void 内置主题清单判定(string name, bool expected)
        => Assert.Equal(expected, ThemeLoader.IsBuiltIn(name));

    [Fact]
    public void 内置主题清单无重复且都有名字与描述()
    {
        // 注意：**取**内嵌 JSON（ThemeLoader.GetBuiltInJson → Avalonia AssetLoader）需要平台服务，
        // 无头环境起不来——那条路径（Assets/*.json 真的打进程序集且能解析）由 Desktop 实跑验收。
        Assert.Equal(ThemeLoader.BuiltInThemes.Length, ThemeLoader.BuiltInThemes.Select(t => t.Name).Distinct().Count());
        Assert.All(ThemeLoader.BuiltInThemes, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
        });
    }
}
