using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GemBasic
{
    // 自定义生长逻辑（v0.4 加入水质）：
    //   晶核放入结晶槽 → 2 天 → 小宝石（随机 30~70g）
    //   宝石留在槽里 → 每天增重一次
    //   槽里必须有 >=100ml 的水；每次结算扣 100ml
    //   纯度 >=70% 加速，<30% 减速
    //   跨过 80/200/400g → 换成中/大/巨阶段
    [HarmonyPatch(typeof(PlayerStore), "EndNight")]
    public static class PatchEndNight
    {
        private const int PricePerKg = 3000;
        private const int MaxWeight = 600;
        private const int CoreDays = 2;
        private const int GemDays = 1;
        private const double BaseGrowRate = 0.04;
        private const int WaterNeedMl = 100;
        private const int PartsPerMl = 1000;

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
                    if (machine == null || machine.identifier != "gem_growth_basic") continue;

                    GameItem water = null;
                    bool warned = false;
                    var children = GetMachineItems(machine, ref water);
                    if (children == null) continue;

                    for (int j = 0; j < children.Count; j++)
                    {
                        var item = children[j];
                        if (item == null) continue;

                        if (item.IsTag("GEM_CORE"))
                            TickCore(machine, item, water, ref warned);
                        else if (item.IsTag("GEM_CULTURED") || IsCulturedId(item.identifier))
                            TickGem(machine, item, water, ref warned);
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error("[GemBasic] 生长 tick 失败: " + e);
            }
        }

        private static void TickCore(GameItem machine, GameItem core, GameItem water, ref bool warned)
        {
            if (!HasWater(water)) { WarnNoWater(ref warned); return; }

            int day = GetInt(core, "GEM_GROW_DAY");
            int need = GetInt(core, "GEM_GROW_NEED");
            if (need <= 0) need = CoreDays;

            day++;
            if (day < need)
            {
                SetInt(core, "GEM_GROW_DAY", day);
                SetInt(core, "GEM_GROW_NEED", need);
                return;
            }

            int weight = Rng.Next(30, 71);
            var info = ReadWater(water);
            string mineral = DetermineMineral(core);
            string color = DetermineColor(mineral, info);
            if (core.identifier == "gem_core_ruby") color = "red";
            var gem = DirectoryMaster.Item(SmallId(mineral, color), true);
            if (gem == null) return;

            PrepareGem(gem, weight, info, mineral, color);

            bool isStack = core.unitCount > 1;
            if (!PlaceAndConsume(machine, gem, core, !isStack))
            {
                gem.Destroy();
                return;
            }

            if (isStack)
            {
                core.unitCount = core.unitCount - 1;
                try { core.Validate(); } catch { }
                SetInt(core, "GEM_GROW_DAY", 0);
            }

            ConsumeWater(water);
            MelonLogger.Msg($"[GemBasic] 晶核长成{GemName(mineral, color)}，{weight}g（水纯度 {PurityText(info)}，{ScoreText(info)}）");
        }

        private static void TickGem(GameItem machine, GameItem gem, GameItem water, ref bool warned)
        {
            int weight = GeneralHelper.GetWeight(gem);
            if (weight <= 0)
            {
                weight = Rng.Next(30, 71);
                ApplyWeight(gem, weight, true);
                UpdateDescription(gem, weight);
            }

            if (weight >= MaxWeight)
            {
                SetInt(gem, "GEM_GROW_DAY", 0);
                return;
            }

            if (!HasWater(water)) { WarnNoWater(ref warned); return; }

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
                gem.SetName(GemName(mineral, color) + "（" + StageName(gem.identifier) + "）");
            }

            double rate = info.Purity >= 70 ? 0.06 : (info.Purity < 30 ? 0.02 : BaseGrowRate);
            // 有机物只当杂质：不参与颜色，只压低生长速度
            int inorganic = info.Metal + info.Mineral + info.Chemical;
            int organic = info.PlantMatter + info.OrganicWaste;
            double impurity = organic / (double)Math.Max(1, inorganic + organic);
            rate *= Math.Max(0.4, 1.0 - 0.6 * impurity);

            int grow = Math.Max(1, (int)Math.Round(weight * rate));
            int newWeight = Math.Min(MaxWeight, weight + grow);
            string targetId = StageIdForWeight(newWeight, mineral, color);

            if (targetId != gem.identifier)
            {
                var upgraded = DirectoryMaster.Item(targetId, true);
                if (upgraded != null)
                {
                    PrepareGem(upgraded, newWeight, info, mineral, color);
                    if (PlaceAndConsume(machine, upgraded, gem))
                    {
                        ConsumeWater(water);
                        MelonLogger.Msg($"[GemBasic] {GemName(mineral, color)}长大到 {newWeight}g，阶段 {targetId}（水纯度 {PurityText(info)}）");
                        return;
                    }
                    upgraded.Destroy();
                }
                SetInt(gem, "GEM_GROW_DAY", 0);
                return;
            }

            ApplyWeight(gem, newWeight, false);
            SetWaterInfo(gem, info);
            UpdateDescription(gem, newWeight);
            gem.Validate();
            ConsumeWater(water);
            SetInt(gem, "GEM_GROW_DAY", 0);
            MelonLogger.Msg($"[GemBasic] {GemName(mineral, color)}继续长大到 {newWeight}g（水纯度 {PurityText(info)}，杂质 {impurity * 100:0}%）");
        }

        private static void PrepareGem(GameItem gem, int weight, WaterInfo info, string mineral, string color)
        {
            gem.EnableTag("GEM_CULTURED", false);
            // 每颗宝石一个唯一标签，避免同 id 被堆叠到同一格
            gem.EnableTag("GEM_UID_" + Guid.NewGuid().ToString("N"), false);
            SetString(gem, "GEM_MINERAL", mineral);
            SetString(gem, "GEM_COLOR", color);
            gem.SetName(GemName(mineral, color) + "（" + StageName(gem.identifier) + "）");
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

            gem.unitValue = PriceHelper.GetWeightedPrice(grams, PricePerKgFor(MineralOf(gem), ColorOf(gem)));
        }

        private static void UpdateDescription(GameItem gem, int grams)
        {
            string baseDesc = GetString(gem, "GEM_BASE_DESC");
            if (baseDesc == null)
            {
                baseDesc = gem.shortDescription ?? "";
                SetString(gem, "GEM_BASE_DESC", baseDesc);
            }

            string desc = "重量：" + grams + " g";
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

        private static bool HasWater(GameItem water)
        {
            if (water == null) return false;
            try { return WaterHelper.GetCurrentCapacityML(water) >= WaterNeedMl; } catch { return false; }
        }

        private static void ConsumeWater(GameItem water)
        {
            try { WaterHelper.Remove(water, WaterNeedMl * PartsPerMl); }
            catch (Exception e) { MelonLogger.Warning("[GemBasic] 扣水失败: " + e.Message); }
        }

        private static void WarnNoWater(ref bool warned)
        {
            if (warned) return;
            warned = true;
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
            MelonLogger.Msg($"[GemBasic] 放入 {newItem.identifier} → {invName} grid={invIsGrid} index={slot.index}");

            if (destroyOld) oldItem.Destroy();
            return true;
        }

        private static string StageIdForWeight(int weight, string mineral, string color)
        {
            string prefix = "gem_" + mineral + "_" + color + "_cultured";
            if (weight < 80) return prefix;
            if (weight < 200) return prefix + "_m";
            if (weight < 400) return prefix + "_l";
            return prefix + "_xl";
        }

        private static string StageName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "小";
            if (id.EndsWith("_xl")) return "巨";
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
