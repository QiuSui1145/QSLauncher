namespace QSLauncher.Core.Services;

/// <summary>
/// Turns "优化 性能 小地图" into several related Modrinth queries instead of
/// sending the whole phrase as one literal search term.
/// </summary>
public static class SearchTerms
{
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["优化"] = ["optimization", "performance"],
        ["性能"] = ["performance", "fps"],
        ["流畅"] = ["performance", "fps"],
        ["科技"] = ["technology", "tech"],
        ["机械"] = ["technology", "machinery"],
        ["工业"] = ["technology", "industrial"],
        ["魔法"] = ["magic"],
        ["冒险"] = ["adventure"],
        ["装饰"] = ["decoration"],
        ["家具"] = ["furniture"],
        ["世界"] = ["worldgen"],
        ["地形"] = ["worldgen"],
        ["生成"] = ["worldgen"],
        ["前置"] = ["library"],
        ["库"] = ["library"],
        ["装备"] = ["equipment"],
        ["武器"] = ["weapon"],
        ["工具"] = ["utility", "tools"],
        ["食物"] = ["food"],
        ["农业"] = ["farming"],
        ["运输"] = ["transportation"],
        ["物流"] = ["transportation", "storage"],
        ["小地图"] = ["minimap", "map"],
        ["地图"] = ["map"],
        ["界面"] = ["ui"],
        ["任务"] = ["quest"],
        ["存储"] = ["storage"],
        ["箱子"] = ["storage"],
        ["生物"] = ["mob"],
        ["怪物"] = ["mob"],
        ["结构"] = ["structure"],
        ["地牢"] = ["structure", "dungeon"],
        ["附魔"] = ["enchantment"],
        ["光影"] = ["shader"],
        ["材质"] = ["resourcepack"],
        ["建筑"] = ["building"],
        ["生存"] = ["survival"],
        ["背包"] = ["inventory"],
        ["合成"] = ["crafting"],
        ["能源"] = ["energy"],
        ["电力"] = ["energy"],
        ["沉浸"] = ["immersive"],
        ["光影包"] = ["shader"],
        ["整合包"] = ["modpack"],
        ["客户端"] = ["client"],
        ["匠魂"] = ["tinkers", "tconstruct"],
        ["虚无世界"] = ["nevermine", "aoa"],
        ["植物魔法"] = ["botania"],
        ["应用能源"] = ["ae2", "applied energistics"],
        ["通用机械"] = ["mekanism"],
        ["沉浸工程"] = ["immersive engineering"],
        ["热力膨胀"] = ["thermal expansion"],
        ["格雷科技"] = ["gregtech"],
        ["群峦传说"] = ["terrafirmacraft"],
        ["龙之研究"] = ["draconic evolution"],
        ["无尽贪婪"] = ["avaritia"],
        ["神秘时代"] = ["thaumcraft"],
        ["星系"] = ["galacticraft"],
        ["拔刀剑"] = ["slashblade"],
        ["暮色森林"] = ["twilight forest"],
        ["农夫乐事"] = ["farmers delight"],
        ["更好的战斗"] = ["better combat"],
        ["苹果皮"] = ["apple skin"],
        ["配方查询"] = ["jei", "rei"],
        ["物品管理器"] = ["jei", "rei"],
        ["区块"] = ["chunk"],
        ["掉落物"] = ["drops"],
        ["漏斗"] = ["hopper"],
        ["管道"] = ["pipe"],
        ["村庄"] = ["village"],
        ["NPC"] = ["villager"],
        ["温度"] = ["temperature"],
        ["口渴"] = ["thirst"],
        ["真实"] = ["realistic", "survival"],
        ["硬核"] = ["hardcore"],
        ["空岛"] = ["skyblock"],
        ["冒险整合"] = ["adventure modpack"],
        ["科技整合"] = ["technology modpack"],
        ["优化整合"] = ["optimization modpack"]
    };

    public static IReadOnlyList<string> Expand(string? query, int maxTerms = 6)
    {
        var raw = (query ?? string.Empty)
            .Replace('，', ' ')
            .Replace('、', ' ')
            .Replace(',', ' ')
            .Replace('+', ' ')
            .Replace('|', ' ')
            .Replace('/', ' ');

        var pieces = raw.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string value)
        {
            var term = value.Trim();
            if (term.Length == 0 || !seen.Add(term))
            {
                return;
            }

            terms.Add(term);
        }

        foreach (var piece in pieces)
        {
            if (Synonyms.TryGetValue(piece, out var translated))
            {
                foreach (var value in translated)
                {
                    Add(value);
                }
            }
            else
            {
                Add(piece);
            }

            if (terms.Count >= maxTerms)
            {
                break;
            }
        }

        if (terms.Count == 0)
        {
            terms.Add(string.Empty);
        }

        return terms.Take(maxTerms).ToList();
    }

    /// <summary>
    /// Interleaves one result list per related term, skipping duplicates, so a
    /// multi-word query cannot be flooded by the highest-download term alone.
    /// </summary>
    public static IReadOnlyList<T> Merge<T>(
        IEnumerable<IReadOnlyList<T>> batches,
        Func<T, string> keySelector,
        int take)
    {
        var queues = batches
            .Where(batch => batch.Count > 0)
            .Select(batch => new Queue<T>(batch))
            .ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<T>();

        while (merged.Count < take && queues.Any(queue => queue.Count > 0))
        {
            foreach (var queue in queues)
            {
                while (queue.Count > 0)
                {
                    var item = queue.Dequeue();
                    if (seen.Add(keySelector(item)))
                    {
                        merged.Add(item);
                        break;
                    }
                }
            }
        }

        return merged;
    }
}
