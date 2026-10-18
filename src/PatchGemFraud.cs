using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GemBasic
{
    // 假标签宝石卖出后：
    //   1. 向 SecData 记一条 fraud 证据（次日 BeginDay 会推进调查进度）；
    //   2. 尝试按原版化学品退货流程，把顾客排进次日退款队列。
    internal static class GemFraud
    {
        private struct Record
        {
            public string ItemId;
            public string ItemName;
            public int UniqueId;
            public string ClientId;
            public string ClientName;
            public string ClientSprite;
            public int SoldPrice;
            public int FakeMod;
            public int RealMod;
            public bool CanDetect;
            public SoldItem Sold;
        }

        private static readonly List<Record> Pending = new List<Record>();

        [HarmonyPatch(typeof(SoldItem), "TrackItem")]
        public static class PatchTrackItem
        {
            [HarmonyPostfix]
            public static void Postfix(GameItem gameItem, StoreClient storeClient, int soldPrice)
            {
                try
                {
                    if (gameItem == null || !gameItem.IsTag("GEM_CULTURED")) return;

                    // 上层珠宝商当场识破的，不算成交欺诈：已经扣过声望，不再加证据/退款。
                    if (gameItem.IsTag("GEM_LABEL_EXPOSED_AT_SALE"))
                    {
                        MelonLogger.Msg($"[GemBasic] 已识破的宝石不再记录欺诈：{gameItem.name}");
                        return;
                    }

                    var feature = GemQuality.FindFeature(gameItem);
                    if (feature == null || feature.fakeCondition == null || feature.realCondition == null) return;

                    // 用 fakeCondition 而不是当前条件：上层珠宝商可能在议价时已经当场揭穿，
                    // 但玩家确实贴过假标签，仍应留下欺诈证据。
                    int fake = feature.fakeCondition.modValue;
                    int real = feature.realCondition.modValue;
                    if (fake <= real || soldPrice <= 0) return;

                    var rec = new Record
                    {
                        ItemId = gameItem.identifier,
                        ItemName = gameItem.name,
                        UniqueId = gameItem.GetUniqueID(),
                        ClientId = storeClient != null ? storeClient.identifier : "",
                        ClientName = storeClient != null ? storeClient.displayName : "",
                        ClientSprite = storeClient != null ? storeClient.spriteName : "",
                        SoldPrice = soldPrice,
                        FakeMod = fake,
                        RealMod = real,
                        CanDetect = storeClient != null && storeClient.IsTag("GEM_JEWELER_DETECT")
                    };

                    // 找到原版刚刚生成的那条 SoldItem，退款流程直接复用它
                    try
                    {
                        var tracker = PlayerStore.Instance != null ? PlayerStore.Instance.soldItemsTracker : null;
                        if (tracker != null)
                        {
                            for (int i = tracker.Count - 1; i >= 0; i--)
                            {
                                var s = tracker[i];
                                if (s != null && s.itemUniqueId == rec.UniqueId) { rec.Sold = s; break; }
                            }
                        }
                    }
                    catch { }

                    Pending.Add(rec);
                    MelonLogger.Msg($"[GemBasic] 假标签交易：{rec.ItemName} 标签+{fake}% 真实+{real}% 售价 {soldPrice}");
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[GemBasic] 记录假标签交易失败: " + e.Message);
                }
            }
        }

        [HarmonyPatch(typeof(PlayerStore), "EndNight")]
        public static class PatchEndNightFraud
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (Pending.Count == 0) return;

                try
                {
                    var store = PlayerStore.Instance;
                    var sec = store != null ? store.secData : null;

                    string crimeId = ResolveFraudCrimeId(sec);

                    for (int i = 0; i < Pending.Count; i++)
                    {
                        var r = Pending[i];
                        if (sec != null)
                        {
                            sec.CommitCrime(crimeId, r.SoldPrice);
                            MelonLogger.Msg($"[GemBasic] 欺诈证据 +{r.SoldPrice}（{r.ItemName}，crime={crimeId}）");
                        }

                        // 中下层不会当场识破，但第二天发现标签问题后仍会回来退款。
                        QueueRefund(r);
                        MelonLogger.Msg($"[GemBasic] {r.ClientName} 次日退款（当场识破={r.CanDetect}）");
                    }
                    if (sec != null)
                        MelonLogger.Msg("[GemBasic] 当前犯罪记录：" + sec.GetSuspectedCrimeDisplay());
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[GemBasic] 结算假标签交易失败: " + e.Message);
                }
                finally
                {
                    Pending.Clear();
                }
            }
        }

        private static string ResolveFraudCrimeId(SecData sec)
        {
            const string fallback = "sec_data_crime_fraud";
            if (sec == null) return fallback;

            string[] candidates =
            {
                "fraud",
                "sec_data_crime_fraud",
                "fraudulent_misrepresentation",
                "misrepresentation",
                "false_statement",
                "fraudulent",
            };

            try
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    string id = candidates[i];
                    string display = sec.GetCrimeString(id);
                    if (!IsOtherCrime(display)) return id;
                }
            }
            catch { }
            return fallback;
        }

        private static bool IsOtherCrime(string display)
        {
            return string.IsNullOrEmpty(display)
                || display == "其他"
                || display == "Other";
        }

        private static void QueueRefund(Record r)
        {
            try
            {
                var sold = r.Sold;
                if (sold == null)
                {
                    sold = new SoldItem();
                    sold.itemId = r.ItemId;
                    sold.itemName = r.ItemName;
                    sold.itemUniqueId = r.UniqueId;
                    sold.clientId = r.ClientId;
                    sold.clientName = r.ClientName;
                    sold.clientSprite = r.ClientSprite;
                    sold.soldPrice = r.SoldPrice;
                }

                sold.CanReturn = true;
                sold.CanReturnAsBuyer = true;
                sold.alwaysFeedback = true;
                sold.feedbackLevel = 2;
                // 游戏没有中层区势力，中下层退款/声望统一走下层的派系。
                sold.factionId = r.CanDetect ? StoreClient.FACTION_UPPER : StoreClient.FACTION_LOWER;

                int gradeDiff = r.FakeMod - r.RealMod;
                bool compensation = gradeDiff >= 75;

                var lines = new Il2CppSystem.Collections.Generic.List<string>();
                // 补偿台词由原版 isAskingCompensation 自己加，这里只放差异说明，避免重复。
                // 上层自己带检测器；中下层没有，第二天是找人验过之后才发现。
                string checkedBy = r.CanDetect ? "我用光谱仪验过了" : "我找人验过了";
                lines.Add(compensation
                    ? checkedBy + "，这标签和实际品质差了不止一个等级。我要退款！"
                    : checkedBy + "，这标签和实际品质对不上。我要退款！");

                var related = new Il2CppSystem.Collections.Generic.List<SoldItem>();
                // 原版这个参数是按“声望变化”使用，负数才是扣声望；
                // 传正数会在拒绝退款时给玩家加声望。
                ChemicalFeedbackHelper.QueueFeedbackClient(sold, related, lines, compensation, -10);
                MelonLogger.Msg($"[GemBasic] 已排队次日退款/赔偿：{r.ClientName} / {r.ItemName}（差价 {gradeDiff}）");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 排队退款失败: " + e.Message);
            }
        }
    }

    // 中下层顾客可能因为现金/预算不足，退款对话只给“拒绝”选项。
    // 给宝石退款客户补足现金/预算，确保“接受退款”选项存在。
    [HarmonyPatch(typeof(StoreClientListSpec), "CreateChemicalFeedbackClient")]
    public static class PatchGemRefundClient
    {
        [HarmonyPostfix]
        public static void Postfix(SoldItem soldItem, ref StoreClient __result)
        {
            try
            {
                if (soldItem == null || __result == null) return;
                string id = soldItem.itemId ?? "";
                if (!id.StartsWith("gem_") || !id.Contains("_cultured")) return;

                int amount = Math.Max(500, soldItem.soldPrice * 3);
                __result.clientCash = Math.Max(__result.clientCash, amount);
                __result.useClientBudget = true;
                __result.SetBudget(amount);
                __result.CanReturn = true;
                __result.CanReturnAsBuyer = true;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 补足退款顾客预算失败: " + e.Message);
            }
        }
    }
}
