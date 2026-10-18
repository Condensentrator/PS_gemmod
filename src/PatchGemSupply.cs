using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GemBasic
{
    // 原版的霓虹灯牌：放进展示柜，第二天会有对应类型的供货商上门。
    // 这里做一个晶核灯牌，开门时检测展示柜，有牌就排一个晶核供货商。
    internal static class GemSupply
    {
        internal const string SignId = "gem_sign";
        internal const string SupplierId = "gem_core_supplier";
        private const string SignTag = "GEM_SIGN_IN_SHOWCASE";
        internal static bool SpawnedToday;

        // 原版供货商到店时会把模板货物直接摆上销售桌；我们在这个窗口里拦掉模板货，
        // 再自己摆晶核。CharacterForge 的 ReplaceTemplateStock 用的也是同一套时机。
        [ThreadStatic]
        internal static bool AddingStock;

        private static readonly HashSet<string> StockedVisits = new HashSet<string>();
        private static int StockedDay = int.MinValue;

        // 供货商每天带来的货。晶核是一次性消耗品，主要靠这里补。
        private static readonly (string Id, int Count)[] Stock =
        {
            ("gem_growth_basic", 1),
            ("gem_autoclave", 1),
            ("gem_core", 5),
            ("gem_core_ruby", 2),
            ("gem_core_beryl", 4),
            ("gem_core_quartz", 4),
        };

        internal static void Reset()
        {
            SpawnedToday = false;
        }

        internal static bool HasSignInDisplayCase()
        {
            bool apiWorked = false;
            try
            {
                bool found = ShowcaseHelper.IsShowcaseContainItemById(SignId);
                apiWorked = true;
                if (found) return true;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 查询展示柜灯牌失败: " + e.Message);
            }

            // 原版接口可用时以它为准；调用异常才退回展示柜回调的标记。
            if (apiWorked) return false;

            try
            {
                var store = PlayerStore.Instance;
                var all = store != null ? store.FindAllItem(true) : null;
                if (all == null) return false;
                for (int i = 0; i < all.Count; i++)
                {
                    var item = all[i];
                    if (item != null && item.identifier == SignId && item.IsTag(SignTag))
                        return true;
                }
            }
            catch { }
            return false;
        }

        internal static StoreClient TrySpawn()
        {
            if (SpawnedToday) return null;
            if (!HasSignInDisplayCase())
            {
                return null;
            }

            StoreClient client = null;
            try { client = StoreClientList.CreateSupplier(); }
            catch (Exception e) { MelonLogger.Warning("[GemBasic] CreateSupplier 失败: " + e.Message); }
            if (client == null)
            {
                try { client = StoreClientListSupplier.CreateScavMaterialSeller(); }
                catch (Exception e) { MelonLogger.Warning("[GemBasic] ScavMaterialSeller 兜底失败: " + e.Message); }
            }
            if (client == null)
            {
                MelonLogger.Warning("[GemBasic] 晶核供货商创建失败，原版工厂都返回空");
                return null;
            }

            ApplyIdentity(client);
            SpawnedToday = true;
            MelonLogger.Msg($"[GemBasic] 晶核灯牌吸引供货商到访：{client.identifier}/{client.displayName}，intent={client.clientIntent}");
            return client;
        }

        internal static bool IsSupplier(StoreClient client)
        {
            return client != null && client.identifier == SupplierId;
        }

        internal static bool IsCurrentSupplier()
        {
            try
            {
                var store = PlayerStore.Instance;
                var instance = store != null ? store.currentClientInstance : null;
                var blueprint = instance != null ? instance.GetClientBlueprint() : null;
                return IsSupplier(blueprint);
            }
            catch
            {
                return false;
            }
        }

        internal static void ApplyIdentity(StoreClient client)
        {
            if (client == null) return;
            client.identifier = SupplierId;
            client.displayName = "晶核供货商";
            client.clientFaction = StoreClient.FACTION_LOWER;
            client.attractedType = "material";
            client.isRequireVariety = false;
            client.CanExposeChem = false;
            client.AddTag("GEM_SUPPLIER");

            // 原版供货商模板自带加价；这里两个修正都归零，按物品基础价卖。
            client.buyPriceModifier = 0;
            client.sellPriceModifier = 0;

            SetDialogue(client);
        }

        internal static void SetDialogue(StoreClient client)
        {
            string[] lines =
            {
                "我是跑矿区的，收了一批不错的晶核。",
                "听说你在用晶核养石头。",
                "结晶槽和几种晶核都带来了，价格好商量。",
            };

            try
            {
                // 已经装过就不再重复插入（StartMainDialogue 会反复调用）。
                var existing = client.mainDialogue;
                if (existing != null && existing.plainText == lines[0])
                    return;

                // 整条替换原版供货商台词；只保留最后一段上的交易选项。
                Dialogue oldLast = existing;
                while (oldLast != null && oldLast.nextDialogue != null)
                    oldLast = oldLast.nextDialogue;

                Dialogue first = null;
                Dialogue current = null;
                for (int i = 0; i < lines.Length; i++)
                {
                    var node = new Dialogue();
                    node.title = "";
                    node.priority = 5;
                    node.isMainDialog = i == 0;
                    node.plainText = lines[i];
                    node.text = new RichTextBuilder().AddLine(lines[i]);

                    if (first == null) { first = node; current = node; }
                    else { current.nextDialogue = node; current = node; }
                }

                if (current != null && oldLast != null)
                    current.choices = oldLast.choices;

                client.mainDialogue = first;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 设置供货商台词失败: " + e.Message);
            }
        }

        // 与原版/CharacterForge 一致：供货商的货用 AddDirectSellingItemToTable(..., isOwend:false)
        // 直接摆到销售桌上，而不是塞进客户 gridInv。
        // 原版给供货商货物挂的“顾客折扣/供货商加价”价格特性；这里清零并禁用。
        private static void NeutralizePriceFeatures(GameItem item)
        {
            try
            {
                var features = item != null ? item.itemFeatures : null;
                if (features == null) return;
                for (int i = 0; i < features.Count; i++)
                {
                    var f = features[i];
                    if (f == null) continue;
                    string id = f.identifier ?? "";
                    string cat = f.category ?? "";
                    string disp = f.publicDisplay ?? "";
                    bool priceFeature =
                        id.IndexOf("supplier_markup", StringComparison.OrdinalIgnoreCase) >= 0
                        || cat.IndexOf("supplier_markup", StringComparison.OrdinalIgnoreCase) >= 0
                        || id.IndexOf("client_discount", StringComparison.OrdinalIgnoreCase) >= 0
                        || cat.IndexOf("client_discount", StringComparison.OrdinalIgnoreCase) >= 0
                        || disp.Contains("供货商加价")
                        || disp.Contains("顾客折扣");
                    if (!priceFeature) continue;

                    f.valueModifier = 0;
                    f.isDisabled = true;
                }
            }
            catch { }
        }

        internal static void AddStock(PlayerStore store)
        {
            if (store == null) return;

            try
            {
                int day = Math.Max(0, StoreStation.GetDayCounter());
                var instance = store.currentClientInstance;
                string key = day + ":" + SupplierId + ":" + (instance != null ? instance.GetHashCode() : 0);
                lock (StockedVisits)
                {
                    if (day != StockedDay)
                    {
                        StockedVisits.Clear();
                        StockedDay = day;
                    }
                    if (!StockedVisits.Add(key)) return;
                }

                AddingStock = true;
                try
                {
                    int added = 0;
                    for (int s = 0; s < Stock.Length; s++)
                    {
                        for (int n = 0; n < Stock[s].Count; n++)
                        {
                            var item = DirectoryMaster.Item(Stock[s].Id, true);
                            if (item == null)
                            {
                                MelonLogger.Warning("[GemBasic] 供货商找不到物品: " + Stock[s].Id);
                                continue;
                            }
                            store.AddDirectSellingItemToTable(item, false, false, false, 0);
                            NeutralizePriceFeatures(item);
                            added++;
                        }
                    }
                    MelonLogger.Msg($"[GemBasic] 晶核供货商到柜，摆出 {added} 件货物");
                }
                finally
                {
                    AddingStock = false;
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 摆放供货商货物失败: " + e.Message);
            }
        }
    }

    // 顾客走到柜台时，原版会把模板货物摆上桌。我们在 postfix 里换成晶核。
    [HarmonyPatch(typeof(StoreClientMono), "OnArrived")]
    public static class PatchGemSupplierArrived
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                if (!GemSupply.IsCurrentSupplier()) return;
                GemSupply.AddStock(PlayerStore.Instance);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 供货商到柜处理失败: " + e.Message);
            }
        }
    }

    // 拦住原版给这个供货商自动摆的模板货，避免晶核和随机杂货混在一起。
    [HarmonyPatch(typeof(PlayerStore), "AddDirectSellingItemToTable")]
    public static class PatchGemSupplierReplaceStock
    {
        [HarmonyPrefix]
        public static bool Prefix(bool isOwend)
        {
            try
            {
                if (GemSupply.AddingStock) return true;
                if (isOwend) return true; // 玩家自己的物品照常上桌
                if (!GemSupply.IsCurrentSupplier()) return true;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(StoreClient), "StartMainDialogue")]
    public static class PatchGemSupplierDialogue
    {
        [HarmonyPrefix]
        public static void Prefix(StoreClient __instance)
        {
            try
            {
                if (!GemSupply.IsSupplier(__instance)) return;
                GemSupply.ApplyIdentity(__instance);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 应用供货商台词失败: " + e.Message);
            }
        }
    }

    // 展示柜登记回调：给灯牌打标记，移出展示柜时清掉。
    [HarmonyPatch(typeof(PlayerStore), "AddedItemToDisplayCase")]
    public static class PatchGemSignAdded
    {
        [HarmonyPostfix]
        public static void Postfix(GameItem gameItem)
        {
            try
            {
                if (gameItem != null && gameItem.identifier == GemSupply.SignId)
                {
                    gameItem.EnableTag("GEM_SIGN_IN_SHOWCASE", false);
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PlayerStore), "RemovedItemToDisplayCase")]
    public static class PatchGemSignRemoved
    {
        [HarmonyPostfix]
        public static void Postfix(GameItem gameItem)
        {
            try
            {
                if (gameItem != null && gameItem.identifier == GemSupply.SignId)
                {
                    if (gameItem.IsTag("GEM_SIGN_IN_SHOWCASE"))
                        gameItem.DisableTag("GEM_SIGN_IN_SHOWCASE", false);
                }
            }
            catch { }
        }
    }

    // 物品区域回调也可以作为展示柜标记的兜底。
    [HarmonyPatch(typeof(GameItem), "OnAddedToShowcaseArea")]
    public static class PatchGemSignAreaAdded
    {
        [HarmonyPostfix]
        public static void Postfix(GameItem __instance)
        {
            try
            {
                if (__instance != null && __instance.identifier == GemSupply.SignId)
                    __instance.EnableTag("GEM_SIGN_IN_SHOWCASE", false);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GameItem), "OnAddedToWeightedArea")]
    public static class PatchGemSignAreaToWeighted
    {
        [HarmonyPostfix]
        public static void Postfix(GameItem __instance)
        {
            try
            {
                if (__instance != null && __instance.identifier == GemSupply.SignId
                    && __instance.IsTag("GEM_SIGN_IN_SHOWCASE"))
                    __instance.DisableTag("GEM_SIGN_IN_SHOWCASE", false);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GameItem), "OnAddedToClientArea")]
    public static class PatchGemSignAreaToClient
    {
        [HarmonyPostfix]
        public static void Postfix(GameItem __instance)
        {
            try
            {
                if (__instance != null && __instance.identifier == GemSupply.SignId
                    && __instance.IsTag("GEM_SIGN_IN_SHOWCASE"))
                    __instance.DisableTag("GEM_SIGN_IN_SHOWCASE", false);
            }
            catch { }
        }
    }
}
