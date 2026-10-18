using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GemBasic
{
    // 测试用发放：基础结晶槽 + 各色晶核 + 测试水。
    internal static class Grant
    {
        private static bool _testWaterGranted;

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

        // 发放：机器 / 晶核 / 测试水。
        private static void GrantBulk(PlayerStore store)
        {
            try
            {
                // 开局各发 1 台：基础结晶槽 + 高压釜。
                if (CountOwned(store, "gem_growth_basic") == 0)
                {
                    var machine = DirectoryMaster.Item("gem_growth_basic", true);
                    if (machine != null)
                    {
                        store.AddDirectSellingItemToTable(machine, true, false, false, 0);
                        MelonLogger.Msg("[GemBasic] 已发放 1 台基础结晶槽");
                    }
                }
                if (CountOwned(store, "gem_autoclave") == 0)
                {
                    var autoclave = DirectoryMaster.Item("gem_autoclave", true);
                    if (autoclave != null)
                    {
                        store.AddDirectSellingItemToTable(autoclave, true, false, false, 0);
                        MelonLogger.Msg("[GemBasic] 已发放 1 台高压釜");
                    }
                    else
                    {
                        MelonLogger.Warning("[GemBasic] 找不到高压釜物品 id: gem_autoclave");
                    }
                }

                // 测试假标签需要标签打印机；没有就补一个
                if (CountOwned(store, "labeler") == 0)
                {
                    var labeler = DirectoryMaster.Item("labeler", true);
                    if (labeler != null)
                    {
                        store.AddDirectSellingItemToTable(labeler, true, false, false, 0);
                        MelonLogger.Msg("[GemBasic] 已补发 1 个标签打印机");
                    }
                    else
                    {
                        MelonLogger.Warning("[GemBasic] 找不到标签打印机物品 id: labeler");
                    }
                }

                // 宝石粉：开局发一个，方便查看贴图。
                if (CountOwned(store, "gem_powder") == 0)
                {
                    var powder = DirectoryMaster.Item("gem_powder", true);
                    if (powder != null)
                    {
                        store.AddDirectSellingItemToTable(powder, true, false, false, 0);
                        MelonLogger.Msg("[GemBasic] 已发放 1 份宝石粉");
                    }
                }

                // 晶核灯牌：放进展示柜后，开门时吸引晶核供货商。
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

                int cores = CountOwned(store, "gem_core") + CountOwned(store, "gem_core_ruby");
                const int coreWanted = 8;
                int addedCores = 0;
                for (int i = cores; i < coreWanted; i++)
                {
                    var core = DirectoryMaster.Item("gem_core", true);
                    if (core != null)
                    {
                        store.AddDirectSellingItemToTable(core, true, false, false, 0);
                        addedCores++;
                    }
                }
                if (addedCores > 0)
                    MelonLogger.Msg($"[GemBasic] 已补发 {addedCores} 枚通用晶核（共 {cores + addedCores} 枚）");

                int berylCores = CountOwned(store, "gem_core_beryl");
                const int berylWanted = 4;
                int addedBeryl = 0;
                for (int i = berylCores; i < berylWanted; i++)
                {
                    var core = DirectoryMaster.Item("gem_core_beryl", true);
                    if (core != null)
                    {
                        store.AddDirectSellingItemToTable(core, true, false, false, 0);
                        addedBeryl++;
                    }
                }
                if (addedBeryl > 0)
                    MelonLogger.Msg($"[GemBasic] 已补发 {addedBeryl} 枚绿柱石晶核（共 {berylCores + addedBeryl} 枚）");

                int quartzCores = CountOwned(store, "gem_core_quartz");
                const int quartzWanted = 4;
                int addedQuartz = 0;
                for (int i = quartzCores; i < quartzWanted; i++)
                {
                    var core = DirectoryMaster.Item("gem_core_quartz", true);
                    if (core != null)
                    {
                        store.AddDirectSellingItemToTable(core, true, false, false, 0);
                        addedQuartz++;
                    }
                }
                if (addedQuartz > 0)
                    MelonLogger.Msg($"[GemBasic] 已补发 {addedQuartz} 枚石英晶核（共 {quartzCores + addedQuartz} 枚）");

                if (!_testWaterGranted)
                {
                    _testWaterGranted = true;
                    // 已有测试水就不再补，避免每次启动都往柜台塞 6 瓶
                    if (!HasTestWater(store))
                    {
                        AddWater(store, WaterPremadeHelper.AccurateRustWater());
                        AddWater(store, MakeWater("CURRENT_PART_MINERAL", 10000));
                        AddWater(store, MakeWater("CURRENT_PART_CHEMICAL_CONTAMINANT", 10000));
                        AddWater(store, MakeWater2("CURRENT_PART_HEAVY_METAL", 5000, "CURRENT_PART_MINERAL", 5000));
                        AddWater(store, MakeWater2("CURRENT_PART_HEAVY_METAL", 5000, "CURRENT_PART_CHEMICAL_CONTAMINANT", 5000));
                        AddWater(store, MakeWater("CURRENT_PART_PLANT_MATTER", 10000));
                        MelonLogger.Msg("[GemBasic] 已发放测试水：锈水(金属) / 富矿物 / 富化学 / 金属+矿物 / 金属+化学 / 富植物(杂质)");
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
