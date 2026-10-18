using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppSystem.Collections.Generic;
using MelonLoader;

namespace GemBasic
{
    // 展示柜里有培育宝石时，按实际价值生成一个分层珠宝商。
    // 不挂在 HandleDisplayCase 上：原版是开门时检测展示柜，这里由 OpenShutter 触发。
    public static class PatchGemJeweler
    {
        private static readonly Random Rng = new Random();
        internal static bool SpawnedToday;

        private static readonly string[] GemIds = BuildGemIds();

        internal static StoreClient TrySpawn()
        {
            try
            {
                if (SpawnedToday) return null;

                int count = GemShowcase.Count;
                long value = GemShowcase.TotalValue();
                if (count <= 0 || value < 200) return null;

                double chance = value >= 2000 ? 0.75 : 0.50;
                if (Rng.NextDouble() > chance) return null;

                string tier;
                StoreClient jeweler;
                if (value >= 2000)
                {
                    tier = "upper";
                    jeweler = StoreClientListMinor.CreateUpperLevelVisitor();
                }
                else if (value >= 800)
                {
                    tier = "middle";
                    jeweler = StoreClientListTier.CreateCitizen();
                }
                else
                {
                    tier = "lower";
                    jeweler = StoreClientListTier.CreateScrapper();
                }
                if (jeweler == null) return null;

                jeweler.identifier = "gem_jeweler_" + tier;
                jeweler.displayName = tier == "upper" ? "上层区珠宝商"
                    : (tier == "middle" ? "中层区珠宝商" : "下层区饰品贩子");
                // 游戏里没有“中层区”势力，中层珠宝商的声望/退款统一归到下层区。
                jeweler.clientFaction = tier == "upper" ? StoreClient.FACTION_UPPER
                    : StoreClient.FACTION_LOWER;
                jeweler.attractedType = "luxury";
                jeweler.clientIntent = StoreClient.ClientIntent.BUY;
                jeweler.clientBuyingTagList = new List<string>();
                jeweler.clientBuyingTagList.Add("GEM_CULTURED");
                jeweler.clientBuyingIdList = new List<string>();
                for (int i = 0; i < GemIds.Length; i++)
                    jeweler.clientBuyingIdList.Add(GemIds[i]);
                jeweler.clientBlackTagList = new List<string>();
                jeweler.clientBlackIdList = new List<string>();
                jeweler.willAcceptItemPreference = null;
                jeweler.CanExposeChem = false;
                jeweler.buyPriceModifier = tier == "upper" ? 110 : (tier == "middle" ? 100 : 85);
                jeweler.sellPriceModifier = 100;
                jeweler.isOfferWholesale = false;
                jeweler.isRequireVariety = false;
                jeweler.useClientBudget = true;

                // 不要走原版 ClientExposeFeature：自定义 ItemFeature 没有 clientExposeDialog，
                // 会触发 GetClientExposeDialog 的 NullReferenceException。
                // 鉴定由 PatchGemFraud 在结算时处理，上层珠宝商会退款/留证据。
                System.Func<ItemFeature, StoreClient, bool> never = NeverExposeGemFeature;
                jeweler.canClientExposeFeature = never;
                if (tier == "upper") jeweler.AddTag("GEM_JEWELER_DETECT");

                int budget = tier == "upper" ? Math.Max(800, (int)Math.Min(int.MaxValue / 2, value * 3))
                    : (tier == "middle" ? Math.Max(600, (int)Math.Min(int.MaxValue / 2, value * 2))
                    : Math.Max(300, (int)Math.Min(int.MaxValue / 2, value * 3 / 2)));
                jeweler.SetBudget(budget);
                ApplyIdentity(jeweler, tier);

                SpawnedToday = true;
                MelonLogger.Msg($"[GemBasic] 开门检测到 {count} 颗展示柜宝石，估值 {value}，{jeweler.displayName}到访（预算 {jeweler.GetBudget()}）");
                return jeweler;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 生成珠宝商失败: " + e.Message);
                return null;
            }
        }

        private static bool CanExposeGemFeature(ItemFeature feature, StoreClient client)
        {
            return feature != null && feature.category == GemQuality.Category;
        }

        private static bool NeverExposeGemFeature(ItemFeature feature, StoreClient client)
        {
            return false;
        }

        internal static void Enqueue(StoreClient jeweler)
        {
            if (jeweler == null) return;
            var store = PlayerStore.Instance;
            var manager = store != null ? store.storeClientManager : null;
            if (manager == null)
            {
                MelonLogger.Warning("[GemBasic] 珠宝商生成成功，但 StoreClientManager 为空");
                return;
            }
            manager.AddNextClient(jeweler);
        }

        internal static string TierOf(StoreClient client)
        {
            string id = client != null ? client.identifier : null;
            if (id == "gem_jeweler_upper") return "upper";
            if (id == "gem_jeweler_middle") return "middle";
            if (id == "gem_jeweler_lower") return "lower";
            return null;
        }

        internal static void ApplyIdentity(StoreClient client, string tier)
        {
            if (client == null || tier == null) return;
            client.displayName = tier == "upper" ? "上层区珠宝商"
                : (tier == "middle" ? "中层区珠宝商" : "下层区饰品贩子");
            client.clientFaction = tier == "upper" ? StoreClient.FACTION_UPPER
                : StoreClient.FACTION_LOWER;
            client.attractedType = "luxury";
            client.CanExposeChem = false;
            if (tier == "upper")
            {
                System.Func<ItemFeature, StoreClient, bool> expose = CanExposeGemFeature;
                client.canClientExposeFeature = expose;
            }
            else
            {
                System.Func<ItemFeature, StoreClient, bool> never = NeverExposeGemFeature;
                client.canClientExposeFeature = never;
            }
            if (!client.IsTag("GEM_JEWELER_DIALOGUE_STARTED"))
                SetDialogue(client, tier);
        }

        internal static void SetDialogue(StoreClient client, string tier)
        {
            string[] lines;
            if (tier == "upper")
            {
                lines = new[]
                {
                    "我是上层区来的珠宝商。",
                    "听说下层区最近有些不错的手工宝石。",
                    "成色好的我按品质收。",
                    "我随身带着检测器，别给我耍花招。",
                };
            }
            else if (tier == "middle")
            {
                lines = new[]
                {
                    "我是中层区来的珠宝商。",
                    "这些石头我收，转手卖给上层区的客人。",
                    "价格合适就成交。",
                };
            }
            else
            {
                lines = new[]
                {
                    "我在下层区摆摊卖饰品。",
                    "这些小石头我收，能卖多少算多少。",
                };
            }

            try
            {
                // 整条替换原版游客的多段台词。每一句单独一个 Dialogue 节点，
                // 由 nextDialogue 串起来，UI 才会一句一句显示，而不是塞成一段。
                var old = client.mainDialogue;
                Dialogue oldLast = old;
                while (oldLast != null && oldLast.nextDialogue != null) oldLast = oldLast.nextDialogue;

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

                // 交易入口在原版最后一段的选项里，挂到我们最后一句上。
                if (current != null && oldLast != null) current.choices = oldLast.choices;

                client.mainDialogue = first;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 设置珠宝商台词失败: " + e.Message);
            }
        }

        private static string[] BuildGemIds()
        {
            string[] bases =
            {
                "gem_corundum_red_cultured",
                "gem_corundum_blue_cultured",
                "gem_corundum_green_cultured",
                "gem_corundum_yellow_cultured",
                "gem_corundum_pink_cultured",
                "gem_beryl_emerald_cultured",
                "gem_beryl_aqua_cultured",
                "gem_beryl_morganite_cultured",
                "gem_beryl_heliodor_cultured",
                "gem_quartz_amethyst_cultured",
                "gem_ruby_cultured",
                "gem_sapphire_cultured",
                "gem_emerald_cultured",
                "gem_corundum_cultured",
                "gem_beryl_cultured",
            };
            string[] suffixes = { "", "_xl" };
            var ids = new System.Collections.Generic.List<string>();
            for (int i = 0; i < bases.Length; i++)
                for (int j = 0; j < suffixes.Length; j++)
                    ids.Add(bases[i] + suffixes[j]);
            return ids.ToArray();
        }
    }

    // 原版在开门时检测展示柜；这里在 OpenShutter 前刷新登记表，开门后生成珠宝商。
    [HarmonyPatch(typeof(StoreShutterButton), "OpenShutter")]
    public static class PatchShutterOpenJeweler
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            PatchGemJeweler.SpawnedToday = false;
            GemShowcase.Refresh();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                var jeweler = PatchGemJeweler.TrySpawn();
                PatchGemJeweler.Enqueue(jeweler);

                var supplier = GemSupply.TrySpawn();
                if (supplier != null) PatchGemJeweler.Enqueue(supplier);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 开门生成珠宝商失败: " + e.Message);
            }
        }
    }

    // 有的流程直接走 OnShutterDoorOpenMorning，不经过 OpenShutter，这里也接一次。
    [HarmonyPatch(typeof(StoreShutterButton), "OnShutterDoorOpenMorning")]
    public static class PatchMorningShutterJeweler
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            PatchGemJeweler.SpawnedToday = false;
            GemShowcase.Refresh();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                var jeweler = PatchGemJeweler.TrySpawn();
                PatchGemJeweler.Enqueue(jeweler);

                var supplier = GemSupply.TrySpawn();
                if (supplier != null) PatchGemJeweler.Enqueue(supplier);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 早间开门生成珠宝商失败: " + e.Message);
            }
        }
    }

    // 客户开始说话时再覆盖一次，避免上层区游客的默认文本覆盖我们在创建时设置的台词。
    [HarmonyPatch(typeof(StoreClient), "StartMainDialogue")]
    public static class PatchGemJewelerDialogue
    {
        [HarmonyPrefix]
        public static void Prefix(StoreClient __instance) => Apply(__instance);

        private static void Apply(StoreClient client)
        {
            try
            {
                string tier = PatchGemJeweler.TierOf(client);
                if (tier == null) return;

                PatchGemJeweler.ApplyIdentity(client, tier);
                PatchGemJeweler.SetDialogue(client, tier);
                client.AddTag("GEM_JEWELER_DIALOGUE_STARTED");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 应用珠宝商台词失败: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(StoreClient), "OnIntroduced")]
    public static class PatchGemJewelerIntroduced
    {
        [HarmonyPostfix]
        public static void Postfix(StoreClient __instance)
        {
            try
            {
                string tier = PatchGemJeweler.TierOf(__instance);
                if (tier == null) return;
                PatchGemJeweler.ApplyIdentity(__instance, tier);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 应用珠宝商身份失败: " + e.Message);
            }
        }
    }

    // 原版 ClientExposeFeature 会去取自定义 ItemFeature 不存在的 clientExposeDialog，
    // 直接放行会 NRE/卡死。这里完全接管我们的珠宝商：
    //   - 上层：手动 ExposeFeature，让标签当场变回真实品质；
    //   - 中层/下层：直接跳过，不具备识别能力。
    [HarmonyPatch(typeof(StoreClient), "ClientExposeFeature")]
    public static class PatchGemClientExposeFeature
    {
        [HarmonyPrefix]
        public static bool Prefix(StoreClient __instance, GameItem gameItem)
        {
            try
            {
                if (__instance == null) return true;
                if (gameItem == null || !gameItem.IsTag("GEM_CULTURED")) return true;

                // 所有针对培育宝石的原版揭穿都跳过，避免自定义 feature 的对话缺失导致 NRE。
                string tier = PatchGemJeweler.TierOf(__instance);
                if (tier == "upper")
                {
                    var feature = GemQuality.FindFeature(gameItem);
                    if (feature != null && feature.realCondition != null && feature.fakeCondition != null
                        && feature.fakeCondition.modValue > feature.realCondition.modValue)
                    {
                        feature.ExposeFeature(true, true);
                        PatchEndNight.RefreshGemDisplay(gameItem);
                        // 标记为“当场识破”，后续不再生成欺诈证据/次日退款。
                        gameItem.EnableTag("GEM_LABEL_EXPOSED_AT_SALE", false);
                        // 原版识破会直接扣一点卖方在该阵营的声望。
                        try
                        {
                            int before = StoreReputation.GetULReputation();
                            StoreReputation.ModCurrentClientReputation(-5, false);
                            int after = StoreReputation.GetULReputation();
                            MelonLogger.Msg($"[GemBasic] 上层区珠宝商当场识破假标签，上层区声望 {before}->{after}");
                        }
                        catch (Exception repEx)
                        {
                            MelonLogger.Warning("[GemBasic] 扣声望失败: " + repEx.Message);
                        }
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 珠宝商鉴定处理失败: " + e.Message);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerStore), "BeginDay")]
    public static class PatchGemJewelerReset
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            PatchGemJeweler.SpawnedToday = false;
            GemSupply.Reset();
        }
    }
}
