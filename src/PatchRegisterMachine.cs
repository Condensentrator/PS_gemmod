using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace GemBasic
{
    // 自己搭结晶槽：
    //   主物品区（5x5）：放晶核/宝石，输入输出共用
    //   槽位：用 LIQUID_CONTAINER_TAG 限定只收水瓶/水罐
    [HarmonyPatch(typeof(MiscItemDirectory), "InitDirectory")]
    public static class PatchRegisterMachine
    {
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();

        internal static Sprite MachineIcon(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Icons.TryGetValue(id, out var cached)) return cached;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("ItemForge.icons." + id + ".png"))
                {
                    if (stream == null)
                    {
                        MelonLogger.Warning("[GemBasic] 找不到内嵌机器图标: " + id);
                        return null;
                    }
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        Il2CppStructArray<byte> imageBytes = ms.ToArray();
                        if (!ImageConversion.LoadImage(tex, imageBytes))
                        {
                            MelonLogger.Warning("[GemBasic] 机器图标加载失败: " + id);
                            return null;
                        }
                        tex.filterMode = FilterMode.Point;
                        var sprite = Sprite.Create(tex,
                            new Rect(0f, 0f, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f), 100f);
                        sprite.name = id;
                        Icons[id] = sprite;
                        return sprite;
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 读取机器图标失败: " + id + " " + e.Message);
            }
            return null;
        }

        internal sealed class MachineParts
        {
            public GameInventory Main;
            public GameInventory Water;
            public GameInventory Module;
        }

        // 读取机器模组提供的百分比加成；模块物品各自带 BONUS_PERCENTAGE_* 标签。
        internal static void GetModuleBonuses(GameItem machine,
            out int quality, out int performance, out int efficiency)
        {
            quality = performance = efficiency = 0;
            if (machine == null) return;
            if (!Parts.TryGetValue(machine.Pointer, out var parts) || parts == null || parts.Module == null) return;

            try
            {
                var items = parts.Module.childItems;
                if (items == null) return;
                for (int i = 0; i < items.Count; i++)
                {
                    var mod = items[i];
                    if (mod == null) continue;
                    quality += TagInt(mod, "BONUS_PERCENTAGE_QUALITY_INT");
                    performance += TagInt(mod, "BONUS_PERCENTAGE_PERFORMANCE_INT");
                    efficiency += TagInt(mod, "BONUS_PERCENTAGE_EFFICIENCY_INT");
                }
            }
            catch { }
        }

        private static int TagInt(GameItem item, string tag)
        {
            try
            {
                var t = item.state.GetTag(tag);
                return t == null ? 0 : t.GetInt();
            }
            catch { return 0; }
        }

        internal static readonly Dictionary<IntPtr, MachineParts> Parts =
            new Dictionary<IntPtr, MachineParts>();

        [HarmonyPostfix]
        public static void Postfix(ItemDirectory __instance)
        {
            try
            {
                if (__instance == null) return;
                System.Func<GameItem> growth = () => CreateMachine();
                ((Directory<GameItem>)(object)__instance).Add("gem_growth_basic", (Il2CppSystem.Func<GameItem>)growth);
                System.Func<GameItem> autoclave = () => CreateAutoclave();
                ((Directory<GameItem>)(object)__instance).Add("gem_autoclave", (Il2CppSystem.Func<GameItem>)autoclave);
            }
            catch (Exception e)
            {
                MelonLogger.Error("[GemBasic] 注册结晶槽失败: " + e);
            }
        }

        private static string NoteText()
        {
            return "基础结晶槽\n\n"
                + "1. 把晶核放入 5×5 材料区。\n"
                + "2. 在水槽里放至少 100ml 的水。\n"
                + "3. 每天结算一次：晶核 2 天后变成宝石，宝石每天长大。\n"
                + "4. 品质由水里的非致色杂质决定；标签可用标签打印机修改。\n\n"
                + "基础数值：\n"
                + "初始重量 30–70g；每天约 4%（水质≥70% 为 6%，<30% 为 2%）；每次耗水 100ml。\n\n"
                + "模组加成：\n"
                + "质量：提高晶核变成宝石时的初始重量。\n"
                + "性能：提高宝石每天的重量增长。\n"
                + "效率：降低每次生长消耗的水。\n";
        }

        private static string AutoclaveNoteText()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "高压釜",
                "",
                "1. 3×3 材料区；水槽只接受幽灵水以上（≥99%）、≥10ml 的水。",
                "2. 晶核 1 天变成宝石，每日增长约 12%。",
                "3. 每次生长有 5% 概率把宝石碎成宝石粉（价值仅 20）。",
                "4. 包裹体级更容易出现，是它最主要的收益点。",
                "",
                "基础数值：",
                "初始重量 10–25g；每天约 12%；每次耗水 10ml。",
                "",
                "模组加成：",
                "质量：提高包裹体级出现概率。",
                "性能：提高每日重量增长。",
                "效率：降低碎裂成宝石粉的概率。",
            });
        }

        private static GridPixelElement MakeTitledBox(string title, GameInventory inv, int w, int h)
        {
            var box = new GridPixelElement(1, 2, true);
            var label = new TagElement(-1, false).SetText(title);
            box.Attach(label.TryCast<PixelElement>(), 0, 0);
            box.Attach(inv.TryCast<PixelElement>(), 0, 1, Math.Max(1, w), Math.Max(1, h));
            return box;
        }

        private static GameItem CreateMachine()
        {
            return BuildMachine("gem_growth_basic", "基础结晶槽", 200, "gem_growth_basic",
                5, 5, 7, 5, 4, "基础结晶槽", NoteText());
        }

        private static GameItem CreateAutoclave()
        {
            return BuildMachine("gem_autoclave", "高压釜", 500, "gem_autoclave",
                3, 3, 5, 5, 4, "高压釜", AutoclaveNoteText());
        }

        private static GameItem BuildMachine(string id, string name, int value, string spriteId,
            int mainW, int mainH, int moduleW, int moduleH, int waterSize,
            string noteTitle, string noteBody)
        {
            var m = ItemDirectory.CreateEmptyItem(id);
            if (m == null) return null;

            m.SetName(name);
            m.SetValue(value);
            // 使用本内容包里内嵌的 48x48 图标；UI 解析时由下面的 Harmony 补丁接管。
            m.spritePath = spriteId;
            m.spriteAtlasPath = "";
            m.spriteChanged = true;
            m._useSpriteFromMod_k__BackingField = true;
            m.shape = new GridShapeBuilder(3, 3).SetDataFill(3, 3, 1).Build();
            m.modifiedShape = m.shape.Clone();
            m.RemoveAllGameItemType();
            m.SetGameItemType("HOUSEHOLD_GOOD");
            m.SetGameItemType("MACHINE");

            // 按原版熔炉的写法手搭单窗口：
            // PixelWindow + GridPixelElement 根布局，库存本身就是 PixelElement，按格子坐标 Attach。
            var window = new PixelWindow(600, 460, true, 8, 8, name);
            var root = new GridPixelElement(2, 2, true);

            var main = new GameGridInventory(mainW, mainH);
            var water = new GameSlotInventory(waterSize, waterSize, false, false);
            var moduleInv = new GameGridInventory(moduleW, moduleH);
            var noteSlot = new GameSlotInventory(2, 2, false, false);

            var mainBox = MakeTitledBox("材料区", main, main.widthPixels, main.heightPixels);
            var moduleBox = MakeTitledBox("模组区", moduleInv, moduleInv.widthPixels, moduleInv.heightPixels);
            var waterBox = MakeTitledBox("水槽", water, water.widthPixels, water.heightPixels);
            var noteBox = MakeTitledBox("备注", noteSlot, noteSlot.widthPixels, noteSlot.heightPixels);

            root.Attach(mainBox.TryCast<PixelElement>(), 0, 0);
            root.Attach(moduleBox.TryCast<PixelElement>(), 1, 0);
            root.Attach(waterBox.TryCast<PixelElement>(), 0, 1);
            root.Attach(noteBox.TryCast<PixelElement>(), 1, 1);

            window.Attach(root.TryCast<PixelElement>());
            window.Validate();
            m.SetContentWindow(window);

            if (main != null)
            {
                try
                {
                    main.identifier = m.identifier;
                    ContainerHelper.InitContainerItem(
                        main, m,
                        (Il2CppSystem.Collections.Generic.List<string>)null,
                        (Il2CppSystem.Collections.Generic.List<string>)null);
                    // 材料区按自定义标签放行：以后新宝石只要带 GEM_ITEM 就能放进来
                    ContainerHelper.AllowOnlyTaggedItemOwn(main, m, "GEM_ITEM", false);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[GemBasic] 主物品区初始化失败: " + e.Message);
                }
            }

            if (water != null)
            {
                try
                {
                    ContainerHelper.AllowOnlyTaggedItemOwn(water, m, "LIQUID_CONTAINER_TAG", false);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[GemBasic] 水槽限制失败: " + e.Message);
                }

                var slot = water.TryCast<GameSlotInventory>();
                if (slot != null)
                {
                    slot.tooltipName = "水槽";

                    // 空槽默认按容器形状自适应；没有容器时会缩成 1x1。
                    // 把空槽占位改成 4x4，方便直接看到可放容器的空间。
                    int oldW = slot.defaultGridWidth;
                    int oldH = slot.defaultGridHeight;
                    int cellW = oldW > 0 ? Math.Max(8, slot.widthPixels / oldW) : 8;
                    int cellH = oldH > 0 ? Math.Max(8, slot.heightPixels / oldH) : 8;
                    slot.defaultGridWidth = 4;
                    slot.defaultGridHeight = 4;
                    try { slot.ResizePixels(cellW * 4, cellH * 4); } catch { }
                }
            }

            // 模组区：只放带 MODULE_TAG 的模组。
            try
            {
                moduleInv.identifier = m.identifier;
                ContainerHelper.InitContainerItem(
                    moduleInv, m,
                    (Il2CppSystem.Collections.Generic.List<string>)null,
                    (Il2CppSystem.Collections.Generic.List<string>)null);
                ContainerHelper.AllowOnlyTaggedItemOwn(moduleInv, m, "MODULE_TAG", false);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 模组区初始化失败: " + e.Message);
            }

            // 备注栏：空槽 + 纸张底图 + 槽位说明，不接收任何物品（和原版熔炉一致）。
            try
            {
                noteSlot.identifier = m.identifier;
                noteSlot.defaultGridWidth = 2;
                noteSlot.defaultGridHeight = 2;

                string atlas = "Items/items_misc";
                string sprite = "handnote";
                var noteTemplate = MachineFurnace.CreateNote();
                if (noteTemplate != null)
                {
                    if (!string.IsNullOrEmpty(noteTemplate.spriteAtlasPath)) atlas = noteTemplate.spriteAtlasPath;
                    if (!string.IsNullOrEmpty(noteTemplate.spritePath)) sprite = noteTemplate.spritePath;
                    noteTemplate.Destroy();
                }

                ContainerHelper.InitContainerItem(
                    noteSlot, m,
                    (Il2CppSystem.Collections.Generic.List<string>)null,
                    (Il2CppSystem.Collections.Generic.List<string>)null);
                noteSlot.SetBackgroundFadeSprite(atlas, sprite, 1f, 1f);
                noteSlot.SetTooltipName(noteTitle + "备注：" + noteBody);
                // 用一个不存在的标签锁死，任何物品都放不进去。
                ContainerHelper.AllowOnlyTaggedItemOwn(noteSlot, m, "GEM_NOTE_SLOT_LOCKED", false);
                noteSlot.Validate();

            }
            catch (Exception e)
            {
                MelonLogger.Warning("[GemBasic] 备注栏初始化失败: " + e.Message);
            }

            Parts[m.Pointer] = new MachineParts { Main = main, Water = water, Module = moduleInv };
            if (main != null && main.TryCast<GameGridInventory>() == null)
                MelonLogger.Warning("[GemBasic] 材料区不是网格，可能存在单格堆叠问题");
            return m;
        }

        internal static GameItem GetSlotItem(GameInventory inv)
        {
            if (inv == null) return null;
            var slot = inv.TryCast<GameSlotInventory>();
            if (slot != null)
                return slot.currentItem != null ? slot.currentItem : slot.childItem;
            var items = inv.childItems;
            if (items != null && items.Count > 0) return items[0];
            return null;
        }
    }

    [HarmonyPatch(typeof(GameItemElement), "ResolveItemSprite")]
    public static class PatchMachineIcon
    {
        [HarmonyPostfix]
        public static void Postfix(GameItemElement __instance, ref Sprite __result)
        {
            try
            {
                if (__instance != null && (__instance.identifier == "gem_growth_basic" || __instance.identifier == "gem_autoclave"))
                {
                    var sprite = PatchRegisterMachine.MachineIcon(__instance.identifier);
                    if (sprite != null) __result = sprite;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GameItemElement), "ResolveSpriteByName")]
    public static class PatchMachineIconByName
    {
        [HarmonyPostfix]
        public static void Postfix(string name, ref Sprite __result)
        {
            try
            {
                if (name == "gem_growth_basic" || name == "gem_autoclave")
                {
                    var sprite = PatchRegisterMachine.MachineIcon(name);
                    if (sprite != null) __result = sprite;
                }
            }
            catch { }
        }
    }
}
