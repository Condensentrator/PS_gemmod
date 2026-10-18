using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GemBasic
{
    // 杰克逊博士的商店：固定把结晶槽/晶核放进他的交易库存。
    internal static class DoctorShopStock
    {
        private static readonly string[] Stock =
        {
            // 灯牌放最前面，避免货架被机器/晶核占满后放不下。
            "gem_sign",
            "gem_autoclave",
            "gem_growth_basic",
            "gem_core",
            "gem_core_beryl",
            "gem_core_quartz",
        };

        internal static bool LooksLikeInventor(GameCharacterItem trader)
        {
            if (trader == null) return false;
            return LooksLike(trader.identifier, trader.name);
        }

        internal static bool LooksLikeClient(StoreClient client)
        {
            if (client == null) return false;
            return LooksLike(client.identifier, client.displayName);
        }

        private static bool LooksLike(string id, string name)
        {
            id = id ?? "";
            name = name ?? "";
            return id.IndexOf("inventor", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("jackson", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("doctor", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("inventor", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("jackson", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("doctor", StringComparison.OrdinalIgnoreCase) >= 0
                || name.Contains("发明家") || name.Contains("杰克逊") || name.Contains("博士");
        }

        internal static void AddTo(GameCharacterItem trader, bool force)
        {
            if (trader == null) return;
            if (!force && !LooksLikeInventor(trader)) return;

            try
            {
                // 商店里“可购买”的库存通常是 barterBuyInventory；为空再退回 barterInventory。
                GameGridScrollableInventory inv = trader.barterBuyInventory;
                if (inv == null) inv = trader.barterInventory;
                if (inv == null)
                {
                    MelonLogger.Warning("[GemBasic] 博士商店商人的 barter 库存为空，无法上架");
                    return;
                }

                int added = Fill(inv);
                try { inv.Validate(); } catch { }
                MelonLogger.Msg($"[GemBasic] 博士商店固定库存：上架 {added} 件（{trader.identifier}/{trader.name}）");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 博士商店上架失败: " + e.Message);
            }
        }

        internal static void AddToClientGrid(GameGridInventory grid)
        {
            if (grid == null) return;
            try
            {
                if (Has(grid, "gem_growth_basic")) return;
                int added = Fill(grid);
                try { grid.Validate(); } catch { }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 博士 barter 货架补货失败: " + e.Message);
            }
        }

        private static int Fill(GameGridScrollableInventory inv)
        {
            int added = 0;
            for (int i = 0; i < Stock.Length; i++)
                if (TryAdd(inv, Stock[i])) added++;
            return added;
        }

        private static int Fill(GameGridInventory inv)
        {
            int added = 0;
            for (int i = 0; i < Stock.Length; i++)
                if (TryAdd(inv, Stock[i])) added++;
            return added;
        }

        private static bool TryAdd(GameGridScrollableInventory inv, string id)
        {
            if (Has(inv, id)) return false;
            var item = DirectoryMaster.Item(id, true);
            if (item == null) { MelonLogger.Warning("[GemBasic] 博士货架找不到物品: " + id); return false; }
            var slot = inv.TryFindOneValidInventorySlot(item);
            if (slot == null) { MelonLogger.Warning("[GemBasic] 博士货架放不下: " + id); item.Destroy(); return false; }
            slot.AcceptUnchecked();
            return true;
        }

        private static bool TryAdd(GameGridInventory inv, string id)
        {
            if (Has(inv, id)) return false;
            var item = DirectoryMaster.Item(id, true);
            if (item == null) { MelonLogger.Warning("[GemBasic] 博士货架找不到物品: " + id); return false; }
            var slot = inv.TryFindOneValidInventorySlot(item);
            if (slot == null) { MelonLogger.Warning("[GemBasic] 博士货架放不下: " + id); item.Destroy(); return false; }
            slot.AcceptUnchecked();
            return true;
        }

        private static bool Has(GameInventory inv, string id)
        {
            var children = inv.childItems;
            if (children == null) return false;
            for (int i = 0; i < children.Count; i++)
            {
                var it = children[i];
                if (it != null && it.identifier == id) return true;
            }
            return false;
        }

        internal static readonly HashSet<string> LoggedTraders = new HashSet<string>();
    }

    [HarmonyPatch(typeof(TraderFactory), "CreateTrader")]
    public static class PatchTraderShop
    {
        [HarmonyPostfix]
        public static void Postfix(GameCharacterItem __result)
        {
            if (__result != null)
            {
                string id = __result.identifier ?? "";
            }
            DoctorShopStock.AddTo(__result, true);
        }
    }

    [HarmonyPatch(typeof(TraderFactory), "CreateDoctor")]
    public static class PatchDoctorShop
    {
        [HarmonyPostfix]
        public static void Postfix(GameCharacterItem __result)
        {
            DoctorShopStock.AddTo(__result, false);
        }
    }

    // 博士商店如果走 BarterOffer，而不是 TraderFactory，也固定补货。
    [HarmonyPatch(typeof(BarterOffer), "InstanciateOffer")]
    public static class PatchInventorBarter
    {
        [HarmonyPostfix]
        public static void Postfix(BarterOffer __instance)
        {
            try
            {
                var client = __instance != null ? __instance.ownerClient : null;
                if (!DoctorShopStock.LooksLikeClient(client)) return;

                var store = PlayerStore.Instance;
                var instance = store != null ? store.currentClientInstance : null;
                var grid = instance != null ? instance.gridInv : null;
                if (grid == null)
                {
                    MelonLogger.Warning("[GemBasic] 博士 barter 当前客户库存为空，无法补货");
                    return;
                }

                DoctorShopStock.AddToClientGrid(grid);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 博士 barter 供货失败: " + e.Message);
            }
        }
    }
}
