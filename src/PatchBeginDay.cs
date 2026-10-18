using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GemBasic
{
    // 测试用发放：基础结晶槽 + 各色晶核 + 测试水。
    internal static class Grant
    {
        internal static void Run()
        {
            try
            {
                var store = PlayerStore.Instance;
                if (store == null) return;

                GemQuality.EnsureRegistered();
                GemShowcase.Refresh();
                RemoveShowcase(store);
                GrantBulk(store);
                EnsureGemTags(store);
            }
            catch (System.Exception e)
            {
                MelonLogger.Error("[GemBasic] 发放测试物品失败: " + e);
            }
        }

        // 旧档里已经发过的成品展示：清掉，给培育材料腾位置。
        private static void RemoveShowcase(PlayerStore store)
        {
            try
            {
                var all = store.FindAllItem(true);
                if (all == null) return;

                var found = new System.Collections.Generic.List<GameItem>();
                for (int i = 0; i < all.Count; i++)
                {
                    var it = all[i];
                    if (it != null && it.identifier == "gem_beryl_aqua_showcase")
                        found.Add(it);
                }
                for (int i = 0; i < found.Count; i++)
                {
                    try { found[i].Destroy(); } catch { }
                }
                if (found.Count > 0)
                    MelonLogger.Msg($"[GemBasic] 已移除 {found.Count} 个成品展示");
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning("[GemBasic] 移除成品展示失败: " + e.Message);
            }
        }

        // 开局只发一块晶核霓虹灯牌；机器/晶核/水由供货商和博士商店提供。
        private static void GrantBulk(PlayerStore store)
        {
            try
            {
                if (CountOwned(store, GemSupply.SignId) == 0)
                {
                    var sign = DirectoryMaster.Item(GemSupply.SignId, true);
                    if (sign != null)
                    {
                        store.AddDirectSellingItemToTable(sign, true, false, false, 0);
                        MelonLogger.Msg("[GemBasic] 已发放 1 块晶核霓虹灯牌");
                    }
                    else
                    {
                        MelonLogger.Warning("[GemBasic] 找不到晶核灯牌物品 id: " + GemSupply.SignId);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning("[GemBasic] 批量发放失败: " + ex.Message);
            }
        }

        // 给旧版本的晶核/宝石补上 GEM_ITEM 标签（材料区按标签放行）
        private static void EnsureGemTags(PlayerStore store)
        {
            try
            {
                var all = store.FindAllItem(true);
                if (all == null) return;
                for (int i = 0; i < all.Count; i++) TagGemTree(all[i]);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning("[GemBasic] 标签迁移失败: " + ex.Message);
            }
        }

        private static void TagGemTree(GameItem item)
        {
            if (item == null) return;
            if ((item.IsTag("GEM_CORE") || item.IsTag("GEM_CULTURED")) && !item.IsTag("GEM_ITEM"))
                item.EnableTag("GEM_ITEM", false);

            if (item.IsTag("GEM_CULTURED"))
                PatchEndNight.MigrateGem(item);

            var kids = item.FindAllChildItems();
            if (kids == null) return;
            for (int i = 0; i < kids.Count; i++) TagGemTree(kids[i]);
        }

        private static void SetPart(GameItem item, string tag, int value)
        {
            if (!item.IsTag(tag)) item.EnableTag(tag, false);
            item.state.GetTag(tag)?.SetInt(value);
        }

        private static GameItem MakeWater(string tag, int value)
        {
            var w = WaterPremadeHelper.AccurateBaseWater();
            if (w != null) SetPart(w, tag, value);
            return w;
        }

        private static GameItem MakeWater2(string t1, int v1, string t2, int v2)
        {
            var w = WaterPremadeHelper.AccurateBaseWater();
            if (w != null)
            {
                SetPart(w, t1, v1);
                SetPart(w, t2, v2);
            }
            return w;
        }

        private static void AddWater(PlayerStore store, GameItem w)
        {
            if (w == null) return;
            // 持久标记：下次启动看到它就不再重复发测试水
            if (!w.IsTag("GEM_TEST_WATER")) w.EnableTag("GEM_TEST_WATER", false);
            store.AddDirectSellingItemToTable(w, true, false, false, 0);
        }

        private static bool HasTestWater(PlayerStore store)
        {
            try
            {
                var all = store.FindAllItem(true);
                if (all == null) return false;
                for (int i = 0; i < all.Count; i++)
                    if (HasTestWaterTag(all[i])) return true;
            }
            catch { }
            return false;
        }

        private static bool HasTestWaterTag(GameItem item)
        {
            if (item == null) return false;
            if (item.IsTag("GEM_TEST_WATER")) return true;

            // 旧档里的测试水没有标记，用成分特征兜底识别
            if (item.IsTag("LIQUID_CONTAINER_TAG") && LooksLikeTestWater(item)) return true;

            var kids = item.FindAllChildItems();
            if (kids == null) return false;
            for (int i = 0; i < kids.Count; i++)
                if (HasTestWaterTag(kids[i])) return true;
            return false;
        }

        private static bool LooksLikeTestWater(GameItem item)
        {
            return GetPart(item, "CURRENT_PART_MINERAL") >= 5000
                || GetPart(item, "CURRENT_PART_CHEMICAL_CONTAMINANT") >= 5000
                || GetPart(item, "CURRENT_PART_HEAVY_METAL") >= 5000
                || GetPart(item, "CURRENT_PART_PLANT_MATTER") >= 5000;
        }

        private static int GetPart(GameItem item, string tag)
        {
            var t = item.state.GetTag(tag);
            return t == null ? 0 : t.GetInt();
        }

        private static int CountOwned(PlayerStore store, string id)
        {
            int n = 0;
            try
            {
                var all = store.FindAllItem(true);
                if (all == null) return 0;
                for (int i = 0; i < all.Count; i++)
                {
                    var it = all[i];
                    if (it != null && it.identifier == id) n++;
                }
            }
            catch { }
            return n;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), "BeginDay")]
    public static class PatchBeginDay
    {
        [HarmonyPostfix]
        public static void Postfix() => Grant.Run();
    }

    [HarmonyPatch(typeof(PlayerStore), "LoadGame")]
    public static class PatchLoadGame
    {
        [HarmonyPostfix]
        public static void Postfix() => Grant.Run();
    }

    [HarmonyPatch(typeof(PlayerStore), "StartNewGame")]
    public static class PatchStartNewGame
    {
        [HarmonyPostfix]
        public static void Postfix() => Grant.Run();
    }
}
