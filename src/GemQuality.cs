using System;
using Il2Cpp;
using Il2CppSystem.Collections.Generic;
using MelonLoader;

namespace GemBasic
{
    // 原版标签体系：
    //   LabelerHelper.GetEditableLabelCount(item) 只统计 FeatureType.Normale、
    //   category 在 ItemFeatureList.CategoryNames 里的 ItemFeature。
    //   下拉选项来自 ItemConditionList.conditionLookup[category]。
    // 这里把宝石品质注册成一个可被标签打印机编辑的分类。
    internal static class GemQuality
    {
        internal const string Category = "item_feature_category_gem_quality";
        internal const string FeatureId = "gem_quality";
        private const string Prefix = "gem_quality_";

        private sealed class Def
        {
            internal string Id;
            internal string Name;
            internal int Mod;
            internal int Rank;
            internal Def(string id, string name, int mod, int rank)
            {
                Id = id; Name = name; Mod = mod; Rank = rank;
            }
        }

        private static readonly Def[] Defs =
        {
            new Def("poor",      "劣质级", -75, 0),
            new Def("common",    "普通级", -45, 1),
            new Def("fine",      "优良级",   0, 2),
            new Def("excellent", "极优级",  75, 3),
            new Def("flawless",  "无瑕级", 125, 4),
            new Def("inclusion", "包裹体级", 200, 5),
        };

        private static readonly System.Collections.Generic.Dictionary<string, ItemCondition> _byId =
            new System.Collections.Generic.Dictionary<string, ItemCondition>();

        private static bool _registered;

        internal static void EnsureRegistered()
        {
            try
            {
                var lookup = ItemConditionList.conditionLookup;
                var names = ItemFeatureList.CategoryNames;
                if (lookup == null || names == null) return;

                if (_registered && lookup.ContainsKey(Category) && names.ContainsKey(Category)
                    && _byId.Count == Defs.Length)
                    return;

                _byId.Clear();
                if (!lookup.ContainsKey(Category))
                {
                    var list = new List<ItemCondition>();
                    for (int i = 0; i < Defs.Length; i++)
                    {
                        var c = MakeCondition(Defs[i]);
                        list.Add(c);
                        _byId[Defs[i].Id] = c;
                    }
                    lookup.Add(Category, list);
                }
                else
                {
                    var list = lookup[Category];
                    for (int i = 0; i < Defs.Length; i++)
                    {
                        var d = Defs[i];
                        ItemCondition found = null;
                        if (list != null)
                        {
                            for (int j = 0; j < list.Count; j++)
                            {
                                var c = list[j];
                                if (c != null && c.identifier == Prefix + d.Id) { found = c; break; }
                            }
                        }
                        _byId[d.Id] = found ?? MakeCondition(d);
                    }
                }

                if (!names.ContainsKey(Category)) names.Add(Category, "宝石品质");
                _registered = true;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 注册宝石品质标签失败: " + e.Message);
            }
        }

        private static ItemCondition MakeCondition(Def d)
        {
            var c = new ItemCondition();
            c.display = d.Name;
            c.identifier = Prefix + d.Id;
            c.modValue = d.Mod;
            c.modValueDisplay = d.Mod;
            c.category = Category;
            c.hiddenAsPublic = false;
            c.compareValue = d.Rank;
            return c;
        }

        internal static ItemCondition GetCondition(string qualityId)
        {
            EnsureRegistered();
            if (string.IsNullOrEmpty(qualityId)) qualityId = "common";
            if (_byId.TryGetValue(qualityId, out var c) && c != null) return c;
            for (int i = 0; i < Defs.Length; i++)
                if (Defs[i].Id == qualityId) return MakeCondition(Defs[i]);
            return MakeCondition(Defs[1]);
        }

        internal static string QualityIdFromCondition(string conditionId)
        {
            if (string.IsNullOrEmpty(conditionId)) return null;
            return conditionId.StartsWith(Prefix, StringComparison.Ordinal)
                ? conditionId.Substring(Prefix.Length)
                : null;
        }

        internal static ItemFeature FindFeature(GameItem gem)
        {
            if (gem == null) return null;
            try { return gem.FindItemFeatureByCategory(Category); }
            catch { return null; }
        }

        // 没有 feature 就建一个：初始标签 = 真实品质。
        internal static ItemFeature EnsureFeature(GameItem gem)
        {
            if (gem == null) return null;
            EnsureRegistered();

            var feature = FindFeature(gem);
            if (feature != null)
            {
                if (feature.fakeCondition == null || feature.realCondition == null)
                {
                    string q = GetString(gem, "GEM_QUALITY");
                    if (string.IsNullOrEmpty(q)) q = "common";
                    var c = GetCondition(q);
                    feature.SetConditions(c, c);
                }
                return feature;
            }

            string realId = GetString(gem, "GEM_QUALITY");
            if (string.IsNullOrEmpty(realId)) realId = "common";
            var real = GetCondition(realId);

            feature = new ItemFeature(Category);
            feature.category = Category;
            feature.identifier = FeatureId;
            feature.featureType = ItemFeature.FeatureType.Normale;
            feature.isExposable = true;
            feature.isPublicHidden = false;
            feature.SetConditions(real, real);
            gem.AddItemFeature(feature);
            return feature;
        }

        internal static void InitFeature(GameItem gem, string qualityId)
        {
            var feature = EnsureFeature(gem);
            if (feature == null) return;
            var real = GetCondition(qualityId);
            feature.SetConditions(real, real);
        }

        // 换阶段：默认标签跟随真实；玩家手动贴过的标签原样继承。
        internal static void InheritFeature(GameItem gem, GameItem previous, string qualityId)
        {
            var feature = EnsureFeature(gem);
            if (feature == null) return;

            var real = GetCondition(qualityId);
            var prev = FindFeature(previous);
            bool custom = previous != null && previous.IsTag("GEM_LABEL_CUSTOM");

            if (custom && prev != null && prev.fakeCondition != null)
            {
                feature.SetConditions(prev.fakeCondition, real);
                gem.EnableTag("GEM_LABEL_CUSTOM", false);
            }
            else
            {
                feature.SetConditions(real, real);
            }
        }

        // 生长只改真实品质；玩家手动贴过的标签保留。
        internal static void SetRealQuality(GameItem gem, string qualityId, string oldRealId)
        {
            var feature = EnsureFeature(gem);
            if (feature == null) return;

            var real = GetCondition(qualityId);
            var fake = feature.fakeCondition;
            bool custom = gem.IsTag("GEM_LABEL_CUSTOM");

            if (fake == null)
            {
                fake = real;
            }
            else
            {
                string fakeQuality = QualityIdFromCondition(fake.identifier);
                if (!custom && !string.IsNullOrEmpty(oldRealId)
                    && !string.IsNullOrEmpty(fakeQuality) && fakeQuality != oldRealId)
                {
                    custom = true;
                }
                if (!custom) fake = real;
            }

            if (custom) gem.EnableTag("GEM_LABEL_CUSTOM", false);
            feature.SetConditions(fake, real);
        }

        internal static string RealQualityId(GameItem gem)
        {
            string q = GetString(gem, "GEM_QUALITY");
            return string.IsNullOrEmpty(q) ? "common" : q;
        }

        internal static string PublicQualityId(GameItem gem)
        {
            var feature = FindFeature(gem);
            var current = feature != null ? feature.GetCurrentItemCondition() : null;
            if (current != null)
            {
                string q = QualityIdFromCondition(current.identifier);
                if (!string.IsNullOrEmpty(q)) return q;
            }
            return RealQualityId(gem);
        }

        internal static int PublicMod(GameItem gem)
        {
            var feature = FindFeature(gem);
            var current = feature != null ? feature.GetCurrentItemCondition() : null;
            if (current != null) return current.modValue;
            return ModForQuality(RealQualityId(gem));
        }

        internal static int ModForQuality(string qualityId)
        {
            for (int i = 0; i < Defs.Length; i++)
                if (Defs[i].Id == qualityId) return Defs[i].Mod;
            return 0;
        }

        internal static bool HasFakeLabel(GameItem gem)
        {
            return PublicQualityId(gem) != RealQualityId(gem);
        }

        private static string GetString(GameItem item, string tag)
        {
            var t = item.state.GetTag(tag);
            return t == null ? null : t.GetString();
        }
    }
}
