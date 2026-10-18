using System;
using MelonLoader;
using UnityEngine.Localization.Settings;

namespace ItemForge.ContentPack
{
    public class ContentPackMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("[GemBasic] 宝石培育测试包已加载");
            InjectGemstoneLocalization();
            InjectGemFeedbackLocalization();
            GemBasic.GemQuality.EnsureRegistered();
        }

        // 游戏实际用的键是 "type_gemstone"，表名 "Mechanic"（不是 TYPE-STRING_*）。
        // 直接往运行时 StringTable 里加条目。
        private static void InjectGemstoneLocalization()
        {
            try
            {
                var db = LocalizationSettings.StringDatabase;
                var locales = LocalizationSettings.AvailableLocales;
                if (db == null || locales == null)
                {
                    MelonLogger.Warning("[GemBasic] 本地化设置未就绪，跳过 type_gemstone 注入");
                    return;
                }

                var list = locales.Locales;
                if (list == null || list.Count == 0)
                {
                    MelonLogger.Warning("[GemBasic] 没有可用语言，跳过 type_gemstone 注入");
                    return;
                }

                int added = 0;
                int missing = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var locale = list[i];
                    if (locale == null) continue;

                    var table = db.GetTable("Mechanic", locale);
                    if (table == null)
                    {
                        missing++;
                        continue;
                    }
                    if (table.GetEntry("type_gemstone") != null) continue;

                    string code = locale.Identifier.Code ?? "";
                    bool zh = code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                    table.AddEntry("type_gemstone", zh ? "宝石" : "Gemstone");
                    added++;
                }

            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 注入 type_gemstone 失败: " + e);
            }
        }

        private static void InjectGemFeedbackLocalization()
        {
            try
            {
                var db = LocalizationSettings.StringDatabase;
                var locales = LocalizationSettings.AvailableLocales;
                if (db == null || locales == null) return;

                var list = locales.Locales;
                if (list == null) return;

                for (int i = 0; i < list.Count; i++)
                {
                    var locale = list[i];
                    if (locale == null) continue;
                    var table = db.GetTable("Dialogue", locale);
                    if (table == null) continue;

                    string code = locale.Identifier.Code ?? "";
                    bool zh = code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

                    if (table.GetEntry("gem_feedback_returning") == null)
                        table.AddEntry("gem_feedback_returning",
                            zh ? "你好，我昨天在你这里买过宝石。" : "Hello, I bought a gem from you yesterday.");

                    if (table.GetEntry("gem_feedback_refund") == null)
                        table.AddEntry("gem_feedback_refund",
                            zh ? "我用光谱仪验过了，这标签和实际品质对不上。我要退款！"
                               : "I checked it with a spectrometer. The label does not match the actual quality. I want a refund!");

                    if (table.GetEntry("gem_feedback_refund_compensation") == null)
                        table.AddEntry("gem_feedback_refund_compensation",
                            zh ? "差了不止一个等级，我要退款，还要赔偿！"
                               : "It is more than one grade off. I want a refund and compensation!");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 注入宝石退款对话失败: " + e);
            }
        }
    }
}
