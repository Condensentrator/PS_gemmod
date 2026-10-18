using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GemBasic
{
    // 追踪玩家展示柜里的培育宝石。
    // 游戏没有“移出展示柜”回调，所以用 GEM_IN_SHOWCASE 标签 + 其他区域回调清理。
    internal static class GemShowcase
    {
        private const string Tag = "GEM_IN_SHOWCASE";
        private static readonly System.Collections.Generic.Dictionary<int, GameItem> Items =
            new System.Collections.Generic.Dictionary<int, GameItem>();

        internal static int Count => Items.Count;

        internal static void Add(GameItem item)
        {
            if (item == null || !item.IsTag("GEM_CULTURED")) return;
            try
            {
                if (!item.IsTag(Tag)) item.EnableTag(Tag, false);
                int id = item.GetUniqueID();
                bool isNew = !Items.ContainsKey(id);
                Items[id] = item;
            }
            catch { }
        }

        internal static void Remove(GameItem item)
        {
            if (item == null) return;
            try
            {
                Items.Remove(item.GetUniqueID());
                if (item.IsTag(Tag)) item.DisableTag(Tag, false);
            }
            catch { }
        }

        private static bool _loggedUnregistered;

        internal static void Refresh()
        {
            try
            {
                var store = PlayerStore.Instance;
                var all = store != null ? store.FindAllItem(true) : null;
                if (all == null) return;

                // 先收集扫描结果，只有确实扫到带标签的宝石才替换；
                // 展示柜物品可能不在 FindAllItem 的遍历范围里，
                // 直接 Clear 会把 AddedItemToDisplayCase 刚登记进来的东西抹掉。
                var scanned = new System.Collections.Generic.Dictionary<int, GameItem>();
                for (int i = 0; i < all.Count; i++)
                {
                    var item = all[i];
                    if (item != null && item.IsTag("GEM_CULTURED") && item.IsTag(Tag))
                        scanned[item.GetUniqueID()] = item;
                }
                if (scanned.Count > 0)
                {
                    Items.Clear();
                    foreach (var pair in scanned) Items[pair.Key] = pair.Value;
                }

                if (!_loggedUnregistered && Items.Count == 0)
                {
                    int unregistered = 0;
                    string sample = "";
                    for (int i = 0; i < all.Count; i++)
                    {
                        var item = all[i];
                        if (item == null || !item.IsTag("GEM_CULTURED")) continue;
                        unregistered++;
                        if (string.IsNullOrEmpty(sample))
                        {
                            string parent = item.parentInventory != null ? item.parentInventory.GetType().Name : "null";
                            sample = $"{item.name} parent={parent} tag={item.IsTag(Tag)}";
                        }
                    }
                    if (unregistered > 0)
                    {
                        _loggedUnregistered = true;
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 扫描展示柜宝石失败: " + e.Message);
            }
        }

        internal static long TotalValue()
        {
            long total = 0;
            foreach (var pair in Items)
            {
                var item = pair.Value;
                if (item == null) continue;
                long v = 0;
                try { v = item.GetCurrentValue(false, true, true); } catch { }
                if (v <= 0) v = Estimate(item.identifier);
                total += Math.Max(1, v);
            }
            return total;
        }

        private static long Estimate(string id)
        {
            if (string.IsNullOrEmpty(id)) return 180;
            if (id.EndsWith("_xl", StringComparison.Ordinal)) return 1700;
            if (id.EndsWith("_l", StringComparison.Ordinal)) return 900;
            if (id.EndsWith("_m", StringComparison.Ordinal)) return 450;
            return 180;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), "AddedItemToDisplayCase")]
    public static class PatchShowcaseAdded
    {
        [HarmonyPostfix]
        public static void Postfix(GameItem gameItem) => GemShowcase.Add(gameItem);
    }

    [HarmonyPatch(typeof(PlayerStore), "RemovedItemToDisplayCase")]
    public static class PatchShowcaseRemoved
    {
        [HarmonyPostfix]
        public static void Postfix(GameItem gameItem) => GemShowcase.Remove(gameItem);
    }

    [HarmonyPatch(typeof(GameItem), "OnAddedToShowcaseArea")]
    public static class PatchShowcaseAreaAdded
    {
        [HarmonyPostfix]
        public static void Postfix(GameItem __instance) => GemShowcase.Add(__instance);
    }

    [HarmonyPatch(typeof(GameItem), "OnAddedToWeightedArea")]
    public static class PatchShowcaseAreaToWeighted
    {
        [HarmonyPostfix]
        public static void Postfix(GameItem __instance) => GemShowcase.Remove(__instance);
    }

    [HarmonyPatch(typeof(GameItem), "OnAddedToClientArea")]
    public static class PatchShowcaseAreaToClient
    {
        [HarmonyPostfix]
        public static void Postfix(GameItem __instance) => GemShowcase.Remove(__instance);
    }

    // 让原版展示柜预测面板显示“宝石”这一项。
    [HarmonyPatch(typeof(ShowcaseHelper), "GetDisplayCasePrediction")]
    public static class PatchShowcasePrediction
    {
        [HarmonyPostfix]
        public static void Postfix(
            ref Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<string, int, string>> __result)
        {
            try
            {
                if (__result == null)
                    __result = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<string, int, string>>();

                if (GemShowcase.Count > 0)
                    __result.Add(new Il2CppSystem.ValueTuple<string, int, string>(
                        "宝石", (int)Math.Min(int.MaxValue, GemShowcase.TotalValue()), "上层区珠宝商"));

                if (GemSupply.HasSignInDisplayCase())
                    __result.Add(new Il2CppSystem.ValueTuple<string, int, string>(
                        "晶核供货", 400, "晶核供货商"));
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 展示柜预测注入失败: " + e.Message);
            }
        }
    }

    // 原版客户生成会按类型调用 CreateClientByStockType；
    // 把自定义“宝石”类型从类型列表里去掉，由 PatchGemJeweler 自己追加客户。
    [HarmonyPatch(typeof(ShowcaseHelper), "GetDisplayCaseType")]
    public static class PatchShowcaseTypeFilter
    {
        [HarmonyPostfix]
        public static void Postfix(ref Il2CppSystem.Collections.Generic.List<string> __result)
        {
            try
            {
                if (__result != null) __result.Remove("宝石");
            }
            catch { }
        }
    }
}
