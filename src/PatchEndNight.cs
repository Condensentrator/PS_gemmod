using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GemBasic
{
    // 自定义生长逻辑（v0.5 加入品质）：
    //   晶核放入结晶槽 → 2 天 → 小宝石（随机 30~70g）
    //   宝石留在槽里 → 每天增重一次
    //   槽里必须有 >=100ml 的水；每次结算扣 100ml
    //   纯度 >=70% 加速，<30% 减速
    //   跨过 80/200/400g → 换成中/大/巨阶段
    //   每次生长按非致色杂质累计平均净度，决定 5 档品质；
    //   高杂质有概率形成价格最高的“包裹体级”
    [HarmonyPatch(typeof(PlayerStore), "EndNight")]
    public static class PatchEndNight
    {
        private const int PricePerKg = 3000;
        private const int MaxWeight = 300;
        private const int CoreDays = 2;
        private const int GemDays = 1;
        private const double BaseGrowRate = 0.04;
        private const int WaterNeedMl = 100;
        private const int PartsPerMl = 1000;

        // 品质：按“非致色杂质”（植物质/有机废物）在非水成分中的占比逐次平均。
        // 致色成分（重金属/矿物/化学污染物）不参与杂质计算。
        private const int ImpScale = 10000;
        private const double InclusionMin = 0.30;
        private const double InclusionMax = 1.00;
        private const double InclusionChance = 0.28;

        private static readonly Random Rng = new Random();

        private struct WaterInfo
        {
            public int Purity;    // 0-100
            public int PurityRaw; // 原始刻度（0-10000）
            public int Mineral;
            public int Metal;
            public int PlantMatter;
            public int OrganicWaste;
            public int Chemical;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                var store = PlayerStore.Instance;
                if (store == null) return;
                var all = store.FindAllItem(true);
                if (all == null) return;

                for (int i = 0; i < all.Count; i++)
                {
                    var machine = all[i];
                    if (machine == null) continue;
                    bool autoclave = machine.identifier == "gem_autoclave";
                    if (machine.identifier != "gem_growth_basic" && !autoclave) continue;

                    GameItem water = null;
                    bool warned = false;
                    var children = GetMachineItems(machine, ref water);
                    if (children == null) continue;

                    for (int j = 0; j < children.Count; j++)
                    {
                        var item = children[j];
                        if (item == null) continue;

                        if (item.IsTag("GEM_CORE"))
                            TickCore(machine, item, water, ref warned, autoclave);
                        else if (item.IsTag("GEM_CULTURED") || IsCulturedId(item.identifier))
                            TickGem(machine, item, water, ref warned, autoclave);
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error("[GemBasic] 生长 tick 失败: " + e);
            }
        }

        private static void TickCore(GameItem machine, GameItem core, GameItem water, ref bool warned, bool autoclave)
        {
            if (!HasWater(machine, water, autoclave)) { WarnNoWater(ref warned, autoclave); return; }

            int day = GetInt(core, "GEM_GROW_DAY");
            int need = GetInt(core, "GEM_GROW_NEED");
            if (need <= 0) need = autoclave ? 1 : CoreDays;

            day++;
            if (day < need)
            {
                SetInt(core, "GEM_GROW_DAY", day);
                SetInt(core, "GEM_GROW_NEED", need);
                return;
            }

            int weight = RollInitialWeight(machine, autoclave);
            PatchRegisterMachine.GetModuleBonuses(machine, out int coreQualityBonus, out _, out _);
            var info = ReadWater(water);
            string mineral = DetermineMineral(core);
            string color = DetermineColor(mineral, info);
            if (core.identifier == "gem_core_ruby") color = "red";
            var gem = DirectoryMaster.Item(SmallId(mineral, color), true);
            if (gem == null) return;

            PrepareGem(gem, weight, info, mineral, color, null, autoclave, coreQualityBonus);

            bool isStack = core.unitCount > 1;
            if (!isStack)
            {
                // 单颗晶核：先销毁晶核腾出它自己的格子，再放新宝石。
                // 这样材料区即使满了，也可以在原位置完成转换。
                core.Destroy();
                if (!PlaceAndConsume(machine, gem, core, false))
                {
                    MelonLogger.Warning("[GemBasic] 材料区没有空位，晶核无法转成宝石");
                    gem.Destroy();
                    return;
                }
            }
            else
            {
                // 成堆晶核必须另外找一格放宝石；真的没位置就暂停这颗。
                if (!PlaceAndConsume(machine, gem, core, false))
                {
                    MelonLogger.Msg("[GemBasic] 材料区已满，成堆晶核暂时无法转成宝石");
                    gem.Destroy();
                    return;
                }
                core.unitCount = core.unitCount - 1;
                try { core.Validate(); } catch { }
                SetInt(core, "GEM_GROW_DAY", 0);
            }

            ConsumeWater(machine, water, autoclave);
            MelonLogger.Msg($"[GemBasic] 晶核长成{GemName(mineral, color)}，{weight}g，品质 {QualityName(GetString(gem, "GEM_QUALITY"))}（水纯度 {PurityText(info)}，{ScoreText(info)}）");
        }

        private static void TickGem(GameItem machine, GameItem gem, GameItem water, ref bool warned, bool autoclave)
        {
            int weight = GeneralHelper.GetWeight(gem);
            if (weight <= 0)
            {
                weight = RollInitialWeight(machine, autoclave);
                ApplyWeight(gem, weight, true);
                UpdateDescription(gem, weight);
            }

            if (weight >= MaxWeight)
            {
                SetInt(gem, "GEM_GROW_DAY", 0);
                return;
            }

            if (!HasWater(machine, water, autoclave)) { WarnNoWater(ref warned, autoclave); return; }

            int day = GetInt(gem, "GEM_GROW_DAY");
            int need = GetInt(gem, "GEM_GROW_NEED");
            if (need <= 0) need = GemDays;

            day++;
            if (day < need)
            {
                SetInt(gem, "GEM_GROW_DAY", day);
                SetInt(gem, "GEM_GROW_NEED", need);
                return;
            }

            var info = ReadWater(water);
            string mineral = MineralOf(gem);
            string color = ColorOf(gem);
            if (!gem.IsTag("GEM_COLOR"))
            {
                SetString(gem, "GEM_COLOR", color);
                SetString(gem, "GEM_MINERAL", mineral);
                UpdateGemName(gem, mineral, color);
            }

            // 游戏里 90–96% 是铁锈水，90% 以下才是沟渠水；按这个区间划档。
            double rate;
            if (autoclave)
                rate = 0.12;
            else
                rate = info.Purity >= 96 ? 0.06 : (info.Purity < 90 ? 0.02 : BaseGrowRate);
            // 有机物只当杂质：不参与颜色，只压低生长速度
            int inorganic = info.Metal + info.Mineral + info.Chemical;
            int organic = info.PlantMatter + info.OrganicWaste;
            double impurity = organic / (double)Math.Max(1, inorganic + organic);
            rate *= Math.Max(0.4, 1.0 - 0.6 * impurity);

            // 模组：性能提高增长率；效率降低高压釜的碎裂概率。
            PatchRegisterMachine.GetModuleBonuses(machine, out int qualityBonus, out int perfBonus, out int effBonus);
            if (perfBonus != 0)
                rate *= 1.0 + perfBonus / 100.0;
            rate = Math.Min(rate, 0.5);

            if (autoclave)
            {
                double powderChance = 0.05 * Math.Max(0.1, 1.0 - effBonus / 100.0);
                if (Rng.NextDouble() < powderChance)
                {
                    var powder = DirectoryMaster.Item("gem_powder", true);
                    if (powder != null && PlaceAndConsume(machine, powder, gem))
                    {
                        ConsumeWater(machine, water, autoclave);
                        MelonLogger.Msg($"[GemBasic] 高压釜碎裂：{GemName(mineral, color)}变成了宝石粉（效率 {effBonus}%，概率 {powderChance * 100:0.0}%）");
                        return;
                    }
                    if (powder != null) powder.Destroy();
                }
            }

            int grow = Math.Max(1, (int)Math.Round(weight * rate));
            int newWeight = Math.Min(MaxWeight, weight + grow);
            string targetId = StageIdForWeight(newWeight, mineral, color);

            if (targetId != gem.identifier)
            {
                var upgraded = DirectoryMaster.Item(targetId, true);
                if (upgraded != null)
                {
                    // 把旧的品质累计带过去，PrepareGem 会把本次水质并入平均值
                    PatchRegisterMachine.GetModuleBonuses(machine, out int gemQualityBonus, out _, out _);
                    PrepareGem(upgraded, newWeight, info, mineral, color, gem, autoclave, gemQualityBonus);
                    if (PlaceAndConsume(machine, upgraded, gem))
                    {
                        ConsumeWater(machine, water, autoclave);
                        MelonLogger.Msg($"[GemBasic] {GemName(mineral, color)}长大到 {newWeight}g，阶段 {targetId}，品质 {QualityName(GetString(upgraded, "GEM_QUALITY"))}（水纯度 {PurityText(info)}）");
                        return;
                    }
                    upgraded.Destroy();
                    // 没有 2x2 空位时不要整颗停摆；继续按当前阶段生长，
                    // 等玩家腾出空间后下一晚会自动升级。
                }
            }

            string oldReal = GetString(gem, "GEM_QUALITY");
            string newQuality = AccumulateQuality(gem, info, autoclave, qualityBonus);
            GemQuality.SetRealQuality(gem, newQuality, oldReal);
            UpdateGemName(gem, mineral, color);
            ApplyWeight(gem, newWeight, false);
            SetWaterInfo(gem, info);
            UpdateDescription(gem, newWeight);
            gem.Validate();
            ConsumeWater(machine, water, autoclave);
            SetInt(gem, "GEM_GROW_DAY", 0);
            MelonLogger.Msg($"[GemBasic] {GemName(mineral, color)}继续长大到 {newWeight}g，真实品质 {QualityName(newQuality)}，标签 {QualityName(GemQuality.PublicQualityId(gem))}（水纯度 {PurityText(info)}）");
        }

        private static void PrepareGem(GameItem gem, int weight, WaterInfo info, string mineral, string color, GameItem previous = null, bool autoclave = false, int qualityBonus = 0)
        {
            gem.EnableTag("GEM_CULTURED", false);
            // 每颗宝石一个唯一标签，避免同 id 被堆叠到同一格
            gem.EnableTag("GEM_UID_" + Guid.NewGuid().ToString("N"), false);
            SetString(gem, "GEM_MINERAL", mineral);
            SetString(gem, "GEM_COLOR", color);

            if (previous != null)
            {
                // 换阶段时继承已有的平均值和特殊品质
                SetInt(gem, "GEM_IMP_SUM", GetInt(previous, "GEM_IMP_SUM"));
                SetInt(gem, "GEM_IMP_COUNT", GetInt(previous, "GEM_IMP_COUNT"));
                if (previous.IsTag("GEM_INCLUSION")) gem.EnableTag("GEM_INCLUSION", false);
            }

            string newQuality = AccumulateQuality(gem, info, autoclave, qualityBonus);
            if (previous == null) GemQuality.InitFeature(gem, newQuality);
            else GemQuality.InheritFeature(gem, previous, newQuality);
            UpdateGemName(gem, mineral, color);
            gem.shortDescription = GemName(mineral, color) + "，人工培育的" + MineralName(mineral) + "。";
            gem.longDescription = gem.shortDescription;
            ApplyWeight(gem, weight, true);
            SetWaterInfo(gem, info);
            UpdateDescription(gem, weight);
            SetInt(gem, "GEM_GROW_DAY", 0);
            SetInt(gem, "GEM_GROW_NEED", GemDays);
        }

        private static void ApplyWeight(GameItem gem, int grams, bool firstTime)
        {
            if (firstTime || GeneralHelper.GetWeight(gem) <= 0)
                GeneralHelper.InitWeight(gem, grams);
            else
                GeneralHelper.SetItemWeight(gem, grams);

            // 品质倍率交给原版 ItemFeature 的价值修正；unitValue 只放重量基础价。
            int basePrice = PriceHelper.GetWeightedPrice(grams, PricePerKgFor(MineralOf(gem), ColorOf(gem)));
            gem.unitValue = Math.Max(1, basePrice);
        }

        private static void UpdateDescription(GameItem gem, int grams)
        {
            string baseDesc = GetString(gem, "GEM_BASE_DESC");
            if (baseDesc == null)
            {
                baseDesc = gem.shortDescription ?? "";
                SetString(gem, "GEM_BASE_DESC", baseDesc);
            }

            string desc = "";
            string quality = GemQuality.PublicQualityId(gem);
            string realQuality = GemQuality.RealQualityId(gem);
            if (!string.IsNullOrEmpty(quality))
            {
                int mod = GemQuality.PublicMod(gem);
                string modText = mod == 0 ? "无修正" : (mod > 0 ? "+" + mod + "%" : mod + "%");
                string prefix = quality == realQuality ? "品质：" : "标签品质：";
                desc = prefix + QualityName(quality)
                    + (quality == "inclusion" ? "（内含物收藏级）" : "")
                    + "（价值 " + modText + "）";

                // 标签与真实一致时才显示净度；贴了假标签就只显示标签。
                if (quality == realQuality)
                {
                    int score = GetInt(gem, "GEM_QUALITY_SCORE");
                    double avg = AverageImpurity(gem);
                    desc += "\n净度 " + (score / 10.0).ToString("0.0") + "%"
                        + "，平均杂质 " + (avg * 100).ToString("0.0") + "%";
                }
                desc += "\n";
            }

            desc += "重量：" + grams + " g";
            if (gem.IsTag("GEM_WATER_PURITY"))
            {
                int p = GetInt(gem, "GEM_WATER_PURITY");
                int mineral = GetInt(gem, "GEM_WATER_MINERAL");
                int metal = GetInt(gem, "GEM_WATER_METAL");
                double pct = p > 100 ? p / 100.0 : p;
                desc += "\n培养液：纯度 " + pct.ToString("0.00") + "%　矿物 " + Ml(mineral) + " ml　重金属 " + Ml(metal) + " ml";
            }
            if (!string.IsNullOrEmpty(baseDesc)) desc += "\n" + baseDesc;

            gem.shortDescription = desc;
            gem.longDescription = desc;
        }

        private static string Ml(int parts) => (parts / 1000.0).ToString("0.#");

        private static void SetWaterInfo(GameItem gem, WaterInfo info)
        {
            SetInt(gem, "GEM_WATER_PURITY", info.PurityRaw);
            SetInt(gem, "GEM_WATER_MINERAL", info.Mineral);
            SetInt(gem, "GEM_WATER_METAL", info.Metal);
        }

        // 杂质只看非水、非致色成分：植物质 + 有机废物。
        // 分母是所有非水成分，这样富矿物/富化学的培养液不会因为“加了颜色”被当成脏水。
        private static double ImpurityRatio(WaterInfo info)
        {
            int organic = info.PlantMatter + info.OrganicWaste;
            int coloring = info.Metal + info.Mineral + info.Chemical;
            int total = organic + coloring;
            if (total <= 0) return 0.0;
            return organic / (double)total;
        }

        private static double AverageImpurity(GameItem gem)
        {
            int sum = GetInt(gem, "GEM_IMP_SUM");
            int count = GetInt(gem, "GEM_IMP_COUNT");
            if (count <= 0) return 0.0;
            return sum / (double)(ImpScale * count);
        }

        // 每次生长把这次水质的杂质比例并入平均，再重算品质。
        // 特殊“包裹体级”只在高杂质区间里按概率触发，一旦出现就固定下来。
        private static string AccumulateQuality(GameItem gem, WaterInfo info, bool autoclave, int qualityBonus)
        {
            int sum = GetInt(gem, "GEM_IMP_SUM");
            int count = GetInt(gem, "GEM_IMP_COUNT");
            sum += (int)Math.Round(ImpurityRatio(info) * ImpScale);
            count++;
            SetInt(gem, "GEM_IMP_SUM", sum);
            SetInt(gem, "GEM_IMP_COUNT", count);

            double avg = sum / (double)(ImpScale * count);
            bool inclusion = gem.IsTag("GEM_INCLUSION");
            int minCount = autoclave ? 1 : 2;
            double minImpurity = autoclave ? 0.10 : 0.15;
            double chance = autoclave
                ? Math.Min(0.90, 0.45 + Math.Max(0, qualityBonus) / 200.0)
                : InclusionChance;
            if (!inclusion && count >= minCount && avg >= minImpurity && avg <= InclusionMax
                && Rng.NextDouble() < chance)
            {
                gem.EnableTag("GEM_INCLUSION", false);
                inclusion = true;
                MelonLogger.Msg($"[GemBasic] 高杂质包裹体形成：平均杂质 {avg * 100:0.0}%");
            }

            string id = QualityId(avg, inclusion);
            SetString(gem, "GEM_QUALITY", id);
            SetInt(gem, "GEM_QUALITY_SCORE", (int)Math.Round((1.0 - avg) * 1000));
            return id;
        }

        private static string QualityId(double avgImpurity, bool inclusion)
        {
            if (inclusion) return "inclusion";
            double purity = 1.0 - avgImpurity;
            if (purity >= 0.90) return "flawless";
            if (purity >= 0.75) return "excellent";
            if (purity >= 0.55) return "fine";
            if (purity >= 0.35) return "common";
            return "poor";
        }

        private static string QualityName(string id)
        {
            if (id == "inclusion") return "包裹体级";
            if (id == "flawless") return "无瑕级";
            if (id == "excellent") return "极优级";
            if (id == "fine") return "优良级";
            if (id == "common") return "普通级";
            if (id == "poor") return "劣质级";
            return "";
        }

        private static void UpdateGemName(GameItem gem, string mineral, string color)
        {
            string quality = GemQuality.PublicQualityId(gem);
            string suffix = string.IsNullOrEmpty(quality) ? "" : " · " + QualityName(quality);
            gem.SetName(GemName(mineral, color) + "（" + StageName(gem.identifier) + "）" + suffix);
        }

        // 标签打印机改完标签后，刷新名字/描述
        internal static void RefreshGemDisplay(GameItem gem)
        {
            if (gem == null || !gem.IsTag("GEM_CULTURED")) return;
            try
            {
                int weight = GeneralHelper.GetWeight(gem);
                if (weight <= 0) weight = 1;
                UpdateGemName(gem, MineralOf(gem), ColorOf(gem));
                UpdateDescription(gem, weight);
                gem.Validate();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 刷新宝石标签失败: " + e.Message);
            }
        }

        // 旧档迁移：把已经乘进 unitValue 的品质倍率退回基础价，交给 feature 修正。
        internal static void MigrateGem(GameItem gem)
        {
            if (gem == null || !gem.IsTag("GEM_CULTURED")) return;
            try
            {
                GemQuality.EnsureFeature(gem);
                string mineral = MineralOf(gem);
                string color = ColorOf(gem);
                SetString(gem, "GEM_BASE_DESC", GemName(mineral, color) + "，人工培育的" + MineralName(mineral) + "。");
                int weight = GeneralHelper.GetWeight(gem);
                if (weight > 0)
                    gem.unitValue = Math.Max(1, PriceHelper.GetWeightedPrice(weight, PricePerKgFor(MineralOf(gem), ColorOf(gem))));
                RefreshGemDisplay(gem);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 迁移宝石品质失败: " + e.Message);
            }
        }

        private static WaterInfo ReadWater(GameItem water)
        {
            int raw = WaterHelper.GetWaterPurity(water);
            int pct = raw > 100 ? raw / 100 : raw;
            return new WaterInfo
            {
                Purity = pct,
                PurityRaw = raw,
                Mineral = GetInt(water, "CURRENT_PART_MINERAL"),
                Metal = GetInt(water, "CURRENT_PART_HEAVY_METAL"),
                PlantMatter = GetInt(water, "CURRENT_PART_PLANT_MATTER"),
                OrganicWaste = GetInt(water, "CURRENT_PART_ORGANIC_WASTE"),
                Chemical = GetInt(water, "CURRENT_PART_CHEMICAL_CONTAMINANT")
            };
        }

        private static string PurityText(WaterInfo info)
        {
            double pct = info.PurityRaw > 100 ? info.PurityRaw / 100.0 : info.PurityRaw;
            return pct.ToString("0.00") + "%";
        }

        private static GameItem FindWater(GameItem machine)
        {
            // 兜底：旧档的普通容器里找水瓶
            var children = machine.FindAllChildItems();
            if (children == null) return null;
            for (int i = 0; i < children.Count; i++)
            {
                var it = children[i];
                if (it == null) continue;
                if (!it.IsTag("LIQUID_CONTAINER_TAG") && !IsKnownWaterId(it.identifier)) continue;
                try
                {
                    if (WaterHelper.GetCurrentCapacityML(it) >= WaterNeedMl) return it;
                }
                catch { }
            }
            return null;
        }

        private static Il2CppSystem.Collections.Generic.List<GameItem> GetMachineItems(GameItem machine, ref GameItem water)
        {
            if (PatchRegisterMachine.Parts.TryGetValue(machine.Pointer, out var parts) && parts != null)
            {
                water = PatchRegisterMachine.GetSlotItem(parts.Water); // 只认专用水槽
                if (parts.Main != null) return parts.Main.childItems;
            }
            water = FindWater(machine); // 旧档兜底
            return machine.FindAllChildItems();
        }

        private static bool IsKnownWaterId(string id)
        {
            return id == "bottled_water" || id == "large_bottled_water" || id == "water_jug";
        }

        // 初始重量：结晶槽 30-70g（质量模组放大）；高压釜 20-50g（质量模组改为防裂）。
        private static int RollInitialWeight(GameItem machine, bool autoclave)
        {
            int weight = autoclave ? Rng.Next(10, 26) : Rng.Next(15, 36);
            if (!autoclave)
            {
                PatchRegisterMachine.GetModuleBonuses(machine, out int qualityBonus, out _, out _);
                if (qualityBonus != 0)
                    weight = Math.Max(1, (int)Math.Round(weight * (1.0 + qualityBonus / 100.0)));
            }
            return weight;
        }

        // 模组“效率”降低每次耗水；高压釜基础耗水 250ml。
        private static int WaterNeedFor(GameItem machine, bool autoclave)
        {
            // 高压釜每轮只耗 10ml，但对水质要求极高；效率模组改为防碎裂。
            if (autoclave) return 10;
            int baseNeed = WaterNeedMl;
            PatchRegisterMachine.GetModuleBonuses(machine, out _, out _, out int efficiencyBonus);
            if (efficiencyBonus <= 0) return baseNeed;
            return Math.Max(1, (int)Math.Round(baseNeed * 100.0 / (100.0 + efficiencyBonus)));
        }

        private static bool HasWater(GameItem machine, GameItem water, bool autoclave)
        {
            if (water == null) return false;
            try
            {
                if (WaterHelper.GetCurrentCapacityML(water) < WaterNeedFor(machine, autoclave)) return false;
                if (autoclave)
                {
                    int raw = WaterHelper.GetWaterPurity(water);
                    int pct = raw > 100 ? raw / 100 : raw;
                    return pct >= 99; // 高压釜只接受 99% 以上的优质水
                }
                return true;
            }
            catch { return false; }
        }

        private static void ConsumeWater(GameItem machine, GameItem water, bool autoclave)
        {
            try { WaterHelper.Remove(water, WaterNeedFor(machine, autoclave) * PartsPerMl); }
            catch (Exception e) { MelonLogger.Warning("[GemBasic] 扣水失败: " + e.Message); }
        }

        private static void WarnNoWater(ref bool warned, bool autoclave)
        {
            if (warned) return;
            warned = true;
            if (autoclave)
                MelonLogger.Msg("[GemBasic] 高压釜需要 ≥99% 纯度、≥10ml 的优质水，生长暂停");
            else
                MelonLogger.Msg("[GemBasic] 结晶槽里没有 ≥" + WaterNeedMl + "ml 的水，生长暂停");
        }

        private static bool PlaceAndConsume(GameItem machine, GameItem newItem, GameItem oldItem, bool destroyOld = true)
        {
            SlotMarker slot = null;

            // 优先用机器主网格的网格原生查找（基类 TryInventorySlot 不认网格坐标，会返回 index=-1）
            if (PatchRegisterMachine.Parts.TryGetValue(machine.Pointer, out var parts) && parts != null && parts.Main != null)
            {
                var grid = parts.Main.TryCast<GameGridInventory>();
                if (grid != null)
                {
                    slot = grid.TryFindOneValidInventorySlot(newItem);
                }
                else
                {
                    slot = parts.Main.TryInventorySlot(newItem, newItem.shape, newItem.state);
                }
            }

            // 兜底：让机器自己在所有子库存里找位置
            if (slot == null) slot = machine.TryFindOneValidInventorySlot(newItem);

            // 旧档：放回原来那个物品区
            if (slot == null)
            {
                GameInventory preferred = null;
                try { preferred = oldItem.parentInventory; } catch { }
                if (preferred != null)
                {
                    var pg = preferred.TryCast<GameGridInventory>();
                    slot = pg != null ? pg.TryFindOneValidInventorySlot(newItem)
                                      : preferred.TryInventorySlot(newItem, newItem.shape, newItem.state);
                }
            }

            if (slot == null) return false;

            slot.AcceptUnchecked();
            try { slot.inventory?.Validate(); } catch { }
            try { newItem.Validate(); } catch { }

            var inv = slot.inventory;
            var invName = inv != null ? inv.GetType().Name : "null";
            bool invIsGrid = inv != null && inv.TryCast<GameGridInventory>() != null;

            if (destroyOld) oldItem.Destroy();
            return true;
        }

        private const int Stage2Weight = 100;

        // 只有这些宝石有第二阶段贴图；没有对应贴图的保持 1x1。
        private static readonly HashSet<string> Stage2Bases = new HashSet<string>
        {
            "gem_corundum_red_cultured",
            "gem_corundum_blue_cultured",
            "gem_corundum_green_cultured",
            "gem_corundum_yellow_cultured",
            "gem_beryl_emerald_cultured",
            "gem_beryl_aqua_cultured",
            "gem_quartz_amethyst_cultured",
            "gem_ruby_cultured",
            "gem_sapphire_cultured",
            "gem_emerald_cultured",
        };

        private static string StageIdForWeight(int weight, string mineral, string color)
        {
            string prefix = "gem_" + mineral + "_" + color + "_cultured";
            if (weight < Stage2Weight) return prefix;
            if (!Stage2Bases.Contains(prefix)) return prefix;
            return prefix + "_xl";
        }

        private static string StageName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "小";
            if (id.EndsWith("_xl")) return "大";
            if (id.EndsWith("_m")) return "中";
            if (id.EndsWith("_l")) return "大";
            return "小";
        }

        private static bool IsCulturedId(string id)
        {
            return !string.IsNullOrEmpty(id)
                && id.StartsWith("gem_")
                && id.Contains("_cultured");
        }

        private static string SmallId(string mineral, string color) => "gem_" + mineral + "_" + color + "_cultured";

        private static string MineralOf(GameItem gem)
        {
            string m = GetString(gem, "GEM_MINERAL");
            if (!string.IsNullOrEmpty(m)) return m;
            if (gem.identifier != null && gem.identifier.Contains("beryl")) return "beryl";
            if (gem.identifier != null && gem.identifier.Contains("quartz")) return "quartz";
            return "corundum";
        }

        private static string ColorOf(GameItem gem)
        {
            string c = GetString(gem, "GEM_COLOR");
            if (!string.IsNullOrEmpty(c)) return c;
            string id = gem.identifier ?? "";
            if (id.Contains("amethyst")) return "amethyst";
            if (id.Contains("sapphire")) return "blue";
            if (id.Contains("emerald")) return "green";
            return "red";
        }

        private static string MineralName(string mineral)
        {
            if (mineral == "beryl") return "绿柱石";
            if (mineral == "quartz") return "石英";
            return "刚玉";
        }

        private static string GemName(string mineral, string color)
        {
            if (mineral == "quartz" && color == "amethyst") return "紫水晶";
            if (mineral == "beryl")
            {
                if (color == "aqua") return "海蓝宝石";
                if (color == "morganite") return "摩根石";
                if (color == "heliodor") return "金绿柱石";
                return "祖母绿";
            }
            if (color == "blue") return "蓝宝石";
            if (color == "green") return "绿色蓝宝石";
            if (color == "yellow") return "黄色蓝宝石";
            if (color == "pink") return "粉色蓝宝石";
            return "红宝石";
        }

        private static int PricePerKgFor(string mineral, string color)
        {
            if (mineral == "quartz") return 3800;
            if (mineral == "beryl")
            {
                if (color == "aqua") return 4200;
                if (color == "morganite") return 4800;
                if (color == "heliodor") return 4000;
                return 5000;
            }
            if (color == "blue") return 3600;
            if (color == "green") return 3200;
            if (color == "yellow") return 4000;
            if (color == "pink") return 4500;
            return 3000;
        }

        private static string DetermineMineral(GameItem core)
        {
            if (core.identifier == "gem_core_beryl") return "beryl";
            if (core.identifier == "gem_core_quartz") return "quartz";
            return "corundum";
        }

        // 颜色只看无机成分（金属/矿物/化学）的比例；有机物不参与颜色
        private static string DetermineColor(string mineral, WaterInfo info)
        {
            if (mineral == "quartz") return "amethyst";

            int metal = info.Metal;
            int mineralPart = info.Mineral;
            int chemical = info.Chemical;
            int total = metal + mineralPart + chemical;
            if (total < 500) return RandomColor(mineral);

            double m = metal / (double)total;
            double n = mineralPart / (double)total;
            double c = chemical / (double)total;

            if (m >= 0.5) return mineral == "beryl" ? "emerald" : "red";
            if (n >= 0.5) return mineral == "beryl" ? "aqua" : "blue";
            if (c >= 0.5) return mineral == "beryl" ? "morganite" : "green";

            if (m >= n && m >= c)
            {
                if (n >= c) return mineral == "beryl" ? "heliodor" : "yellow";
                return mineral == "beryl" ? "morganite" : "pink";
            }
            if (n >= m && n >= c)
            {
                if (m >= c) return mineral == "beryl" ? "heliodor" : "yellow";
                return mineral == "beryl" ? "emerald" : "green";
            }
            if (m >= n) return mineral == "beryl" ? "morganite" : "pink";
            return mineral == "beryl" ? "emerald" : "green";
        }

        private static string RandomColor(string mineral)
        {
            if (mineral == "quartz") return "amethyst";
            if (mineral == "beryl")
            {
                int r = Rng.Next(0, 4);
                return r == 0 ? "emerald" : (r == 1 ? "aqua" : (r == 2 ? "morganite" : "heliodor"));
            }
            int q = Rng.Next(0, 5);
            switch (q)
            {
                case 0: return "red";
                case 1: return "blue";
                case 2: return "green";
                case 3: return "yellow";
                default: return "pink";
            }
        }

        private static string ScoreText(WaterInfo info)
        {
            int organic = info.PlantMatter + info.OrganicWaste;
            return $"金属{info.Metal / 1000.0:0.#} 矿物{info.Mineral / 1000.0:0.#} 化学{info.Chemical / 1000.0:0.#} 有机{organic / 1000.0:0.#} ml";
        }

        private static int GetInt(GameItem item, string tag)
        {
            var t = item.state.GetTag(tag);
            return t == null ? 0 : t.GetInt();
        }

        private static void SetInt(GameItem item, string tag, int value)
        {
            if (!item.IsTag(tag)) item.EnableTag(tag, false);
            item.state.GetTag(tag)?.SetInt(value);
        }

        private static string GetString(GameItem item, string tag)
        {
            var t = item.state.GetTag(tag);
            if (t == null) return null;
            return t.GetString();
        }

        private static void SetString(GameItem item, string tag, string value)
        {
            if (!item.IsTag(tag)) item.EnableTag(tag, false);
            item.state.GetTag(tag)?.SetString(value);
        }
    }
}
