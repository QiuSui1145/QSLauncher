namespace QSLauncher.Core.Services;

/// <summary>
/// Modrinth descriptions are usually English. The launcher keeps the original
/// text for search/reference, but shows a short Chinese functional summary
/// first so Chinese users can understand a result without opening its page.
/// This is deliberately a local glossary, not a network translation service.
/// </summary>
public static class DescriptionLocalizer
{
    private static readonly (string Key, string Text)[] KnownTitles =
    [
        ("sodium", "钠 · 高性能渲染优化，显著提升帧率"),
        ("iris", "Iris · 光影加载器，兼容常用光影包"),
        ("lithium", "锂 · 游戏逻辑与服务端性能优化"),
        ("xaero", "Xaero · 小地图与世界地图"),
        ("journeymap", "旅行地图 · 小地图与路径点"),
        ("jei", "JEI · 物品与配方查询"),
        ("rei", "REI · 物品与配方查询"),
        ("create", "Create · 机械、齿轮与自动化"),
        ("ae2", "应用能源 · 数字存储与自动化网络"),
        ("appliedenergistics", "应用能源 · 数字存储与自动化网络"),
        ("farmersdelight", "农夫乐事 · 农业、烹饪与食物"),
        ("twilightforest", "暮色森林 · 冒险维度与 Boss"),
        ("alexsmobs", "Alex 的生物 · 新增生物与生态"),
        ("curios", "Curios · 饰品栏与装备扩展"),
        ("geckolib", "GeckoLib · 模组动画前置库"),
        ("patchouli", "帕秋莉手册 · 游戏内指南书前置"),
        ("botania", "植物魔法 · 花与魔力科技"),
        ("thermal", "热力 · 机器与能源科技"),
        ("mekanism", "通用机械 · 科技、能源与矿物处理"),
        ("immersiveengineering", "沉浸工程 · 电力与机械工程"),
        ("gregtech", "格雷科技 · 大型工业与科技链"),
        ("tconstruct", "匠魂 · 工具锻造与材料"),
        ("draconicevolution", "龙之研究 · 龙族科技与装备"),
        ("galacticraft", "星系 · 太空探索与火箭"),
        ("adastra", "Ad Astra · 太空与行星探索"),
        ("terrafirmacraft", "群峦传说 · 真实生存与地质"),
        ("bettercombat", "Better Combat · 改进战斗动作"),
        ("appleskin", "苹果皮 · 显示食物与饱和度信息"),
        ("jade", "玉 · 方块与实体信息提示"),
        ("wthit", "WTHIT · 方块信息提示"),
        ("embeddium", "Embeddium · 渲染性能优化"),
        ("ferritecore", "FerriteCore · 降低内存占用"),
        ("modernfix", "ModernFix · 启动与内存优化"),
        ("spark", "Spark · 性能分析工具")
    ];

    private static readonly (string Key, string Text)[] CategoryText =
    [
        ("optimization", "性能优化 · 提升帧率、缩短加载时间"),
        ("technology", "科技 · 机器、能源与自动化"),
        ("magic", "魔法 · 法术、仪式与魔力系统"),
        ("adventure", "冒险 · 探索、战斗与维度"),
        ("worldgen", "世界生成 · 新地形、群系与结构"),
        ("decoration", "装饰 · 建筑方块与家具"),
        ("equipment", "装备 · 武器、护甲与工具"),
        ("food", "食物 · 农业、烹饪与饮食"),
        ("mobs", "生物 · 新增怪物与生态"),
        ("storage", "存储 · 仓库、分类与物流"),
        ("library", "前置库 · 为其他模组提供接口"),
        ("transportation", "运输 · 载具、道路与物流"),
        ("utility", "实用 · 信息提示与便捷工具")
    ];

    private static readonly (string Key, string Text)[] DescriptionHints =
    [
        ("performance", "性能优化 · 提升帧率与降低卡顿"),
        ("fps", "性能优化 · 提升帧率与降低卡顿"),
        ("shader", "光影 · 画面效果与光影支持"),
        ("minimap", "小地图 · 地图、路径点与导航"),
        ("world map", "世界地图 · 探索记录与导航"),
        ("recipe", "配方查询 · 快速查找物品用途"),
        ("storage", "存储 · 仓库与物品管理"),
        ("automation", "自动化 · 机器与物流"),
        ("adventure", "冒险 · 探索与战斗"),
        ("dimension", "维度 · 新的世界与冒险区域"),
        ("decoration", "装饰 · 建筑与家具"),
        ("magic", "魔法 · 法术与仪式"),
        ("technology", "科技 · 机器与能源"),
        ("food", "食物 · 农业与烹饪")
    ];

    public static string Summarize(
        string? title,
        string? description,
        IReadOnlyList<string>? categories = null)
    {
        if (!string.IsNullOrWhiteSpace(description) && ContainsChinese(description))
        {
            return description.Trim();
        }

        var normalizedTitle = (title ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        foreach (var (key, text) in KnownTitles)
        {
            if (normalizedTitle.Contains(key, StringComparison.Ordinal))
            {
                return text;
            }
        }

        if (categories is not null)
        {
            foreach (var (key, text) in CategoryText)
            {
                if (categories.Any(category =>
                        category.Contains(key, StringComparison.OrdinalIgnoreCase)))
                {
                    return text;
                }
            }
        }

        var lowerDescription = (description ?? string.Empty).ToLowerInvariant();
        foreach (var (key, text) in DescriptionHints)
        {
            if (lowerDescription.Contains(key, StringComparison.Ordinal))
            {
                return text;
            }
        }

        return string.IsNullOrWhiteSpace(description)
            ? "暂无中文简介"
            : "英文简介 · " + description.Trim();
    }

    private static bool ContainsChinese(string text) =>
        text.Any(character => character is >= '\u4e00' and <= '\u9fff');
}
