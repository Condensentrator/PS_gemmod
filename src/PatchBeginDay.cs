using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GemBasic
{
    // 测试用发放。当前只发一个海蓝宝石成品展示；批量发放保留在 GrantBulk，
    // 需要时把 Run 里的调用取消注释即可。
    internal static class Grant
    {
        private static bool _testWaterGranted;
        private static bool _showcaseGranted;

        internal static void Run()
        {
            try
            {
                var store = PlayerStore.Instance;
                if (store == null) return;

                // 一次发太多，柜台放不下；先只发展示品。
                GrantShowcase(store);
                // GrantBulk(store);

                EnsureGemTags(store);
            }
            catch (System.Exception e)
            {
                MelonLogger.Error("[GemBasic] 发放测试物品失败: " + e);
            }
        }

        private static void GrantShowcase(PlayerStore store)
        {
            if (_showcaseGranted) return;
            _showcaseGranted = true;

            try
            {
                var show = DirectoryMaster.Item("gem_beryl_aqua_showcase", true);
                if (show != null)
                {
                    show.SetName("海蓝宝石（成品展示）");
                    store.AddDirectSellingItemToTable(show, true, false, false, 0);
                    MelonLogger.Msg("[GemBasic] 已发放海蓝宝石成品展示（2×3）");
                }
            }
            catch (System.Exception e)
            {
                MelonLogger.Warning("[GemBasic] 发放成品展示失败: " + e.Message);
            }
        }

        // 批量发放：机器 / 晶核 / 测试水。
        // 当前 Run 里没有调用，需要时取消注释。
        private static void GrantBulk(PlayerStore store)
        {
            try
            {
                int machines = CountOwned(store, "gem_growth_basic");
                const int machineWanted = 3;
                int addedMachines = 0;
                for (int i = machines; i < machineWanted; i++)
                {
                    var machine = DirectoryMaster.Item("gem_growth_basic", true);
                    if (machine != null)
                    {
                        store.AddDirectSellingItemToTable(machine, true, false, false, 0);
                        addedMachines++;
                    }
                }
                if (addedMachines > 0)
                    MelonLogger.Msg($"[GemBasic] 已补发 {addedMachines} 台结晶槽（共 {machines + addedMachines} 台）");

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
                    AddWater(store, WaterPremadeHelper.AccurateRustWater());
                    AddWater(store, MakeWater("CURRENT_PART_MINERAL", 10000));
                    AddWater(store, MakeWater("CURRENT_PART_CHEMICAL_CONTAMINANT", 10000));
                    AddWater(store, MakeWater2("CURRENT_PART_HEAVY_METAL", 5000, "CURRENT_PART_MINERAL", 5000));
                    AddWater(store, MakeWater2("CURRENT_PART_HEAVY_METAL", 5000, "CURRENT_PART_CHEMICAL_CONTAMINANT", 5000));
                    AddWater(store, MakeWater("CURRENT_PART_PLANT_MATTER", 10000));
                    MelonLogger.Msg("[GemBasic] 已发放测试水：锈水(金属) / 富矿物 / 富化学 / 金属+矿物 / 金属+化学 / 富植物(杂质)");
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
            if (w != null) store.AddDirectSellingItemToTable(w, true, false, false, 0);
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
