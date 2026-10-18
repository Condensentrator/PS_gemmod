using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace GemBasic
{
    // 标签打印机改完下拉选项后，刷新宝石名字/描述。
    [HarmonyPatch(typeof(LabelerElement), "OnDropdownValueChanged")]
    public static class PatchGemLabel
    {
        [HarmonyPostfix]
        public static void Postfix(LabelerElement __instance)
        {
            try
            {
                var feature = __instance.thisFeature;
                var item = feature != null ? feature.GetParentItem() : null;

                if (item != null && item.IsTag("GEM_CULTURED")
                    && feature.fakeCondition != null && feature.realCondition != null
                    && feature.fakeCondition.identifier != feature.realCondition.identifier)
                {
                    // 玩家手动改过标签后，后续生长不再自动同步标签；
                    // 同时清掉“已当场识破”，允许新标签重新走欺诈结算。
                    item.EnableTag("GEM_LABEL_CUSTOM", false);
                    if (item.IsTag("GEM_LABEL_EXPOSED_AT_SALE"))
                        item.DisableTag("GEM_LABEL_EXPOSED_AT_SALE", false);
                }

                PatchEndNight.RefreshGemDisplay(item);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 标签刷新失败: " + e.Message);
            }
        }
    }
}
