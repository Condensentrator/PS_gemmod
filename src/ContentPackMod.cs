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

                MelonLogger.Msg($"[GemBasic] Mechanic 表注入 type_gemstone：{added} 个语言，{missing} 个表未找到");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 注入 type_gemstone 失败: " + e);
            }
        }
    }
}
