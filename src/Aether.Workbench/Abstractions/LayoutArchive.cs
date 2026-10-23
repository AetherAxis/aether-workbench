using System;
using System.Text.Json;

namespace Aether.Workbench.Abstractions;

/// <summary>
/// 持久化布局档的**外层信封**（带版本号）。布局 JSON 存在用户偏好里、跨版本存活；
/// 没有版本号就无法在未来的结构变更时说「这档我不认识，回退默认」——只能靠反序列化失败猜。
/// <para><see cref="Version"/> 与 <see cref="Root"/> 是唯二字段；<see cref="Root"/> 的多态形状（<c>$kind</c>）
/// 由 <see cref="LayoutNode"/> 决定。</para>
/// </summary>
/// <param name="Version">档格式版本（<see cref="CurrentVersion"/> = 当前；更高 = 未来版本，本版不认识）。</param>
/// <param name="Root">布局树根节点。</param>
public sealed record LayoutArchive(int Version, LayoutNode Root)
{
    /// <summary>当前档格式版本。结构不兼容变更时 +1（并在此处写迁移）。</summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// 布局档读写：<b>新档写信封，旧档（裸 <see cref="LayoutNode"/> JSON）照读</b>——升级不清用户布局。
/// 反序列化失败 / 版本高于本版 / 内容为 null 一律返 null（调用方回退默认布局，不崩）。
/// </summary>
public static class LayoutArchiveSerializer
{
    private static readonly JsonSerializerOptions Json = new();

    /// <summary>序列化为带版本的信封。</summary>
    public static string Serialize(LayoutNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return JsonSerializer.Serialize(new LayoutArchive(LayoutArchive.CurrentVersion, root), Json);
    }

    /// <summary>读取布局档。返回 null = 无档/坏档/版本过新（调用方一律回退默认布局）。</summary>
    public static LayoutNode? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            // 先看信封：有 Root 键 = 新档；否则是 2026-10 之前的裸布局树。
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("Root", out _))
                return JsonSerializer.Deserialize<LayoutNode>(json, Json);

            var archive = JsonSerializer.Deserialize<LayoutArchive>(json, Json);
            if (archive is null || archive.Root is null) return null;
            if (archive.Version > LayoutArchive.CurrentVersion) return null;   // 未来版本 → 不猜，回退默认
            return archive.Root;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // 未知 $kind（未来新增节点类型）：同样视为不认识 → 回退默认
            return null;
        }
    }

    /// <summary>选中 tab 索引夹到合法区间（坏档里 99 / -1 不能把渲染打崩）。</summary>
    public static int ClampSelectedIndex(int index, int regionCount)
        => regionCount <= 0 ? 0 : Math.Clamp(index, 0, regionCount - 1);
}
