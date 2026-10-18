using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;

namespace GemBasic
{
    // 自己搭结晶槽：
    //   主物品区（5x5）：放晶核/宝石，输入输出共用
    //   槽位：用 LIQUID_CONTAINER_TAG 限定只收水瓶/水罐
    [HarmonyPatch(typeof(MiscItemDirectory), "InitDirectory")]
    public static class PatchRegisterMachine
    {
        internal sealed class MachineParts
        {
            public GameInventory Main;
            public GameInventory Water;
        }

        internal static readonly Dictionary<IntPtr, MachineParts> Parts =
            new Dictionary<IntPtr, MachineParts>();

        [HarmonyPostfix]
        public static void Postfix(ItemDirectory __instance)
        {
            try
            {
                if (__instance == null) return;
                System.Func<GameItem> managed = () => CreateMachine();
                Il2CppSystem.Func<GameItem> factory = managed;
                ((Directory<GameItem>)(object)__instance).Add("gem_growth_basic", factory);
                MelonLogger.Msg("[GemBasic] 已注册基础结晶槽");
            }
            catch (Exception e)
            {
                MelonLogger.Error("[GemBasic] 注册结晶槽失败: " + e);
            }
        }

        private static GameItem CreateMachine()
        {
            var m = ItemDirectory.CreateEmptyItem("gem_growth_basic");
            if (m == null) return null;

            m.SetName("基础结晶槽");
            m.SetValue(300);
            m.SetSprite("Items/items_tool2", "furnace");
            m.shape = new GridShapeBuilder(3, 3).SetDataFill(3, 3, 1).Build();
            m.modifiedShape = m.shape.Clone();
            m.RemoveAllGameItemType();
            m.SetGameItemType("HOUSEHOLD_GOOD");
            m.SetGameItemType("MACHINE");

            var t = DirectoryUtils.CreateDoubleInventoryWindowSlotInput(5, 5, true);
            var window = t.Item1;
            var invA = t.Item2;
            var invB = t.Item3;

            GameInventory main = invA;
            GameInventory water = invB;

            var gridA = invA != null ? invA.TryCast<GameGridInventory>() : null;
            var gridB = invB != null ? invB.TryCast<GameGridInventory>() : null;
            if (gridA == null && gridB != null)
            {
                main = invB;
                water = invA;
            }
            else if (gridA != null && gridB == null)
            {
                main = invA;
                water = invB;
            }
            else
            {
                var slotA = invA != null ? invA.TryCast<GameSlotInventory>() : null;
                var slotB = invB != null ? invB.TryCast<GameSlotInventory>() : null;
                if (slotA != null && slotB == null)
                {
                    water = invA;
                    main = invB;
                }
                else if (slotB != null)
                {
                    water = invB;
                    main = invA;
                }
            }

            if (main == null || main.TryCast<GameGridInventory>() == null)
            {
                try
                {
                    var t2 = DirectoryUtils.CreateDoubleInventoryWindow(5, 5, true);
                    if (t2.Item1 != null && t2.Item2 != null && t2.Item3 != null)
                    {
                        window = t2.Item1;
                        main = t2.Item2;
                        water = t2.Item3;
                        MelonLogger.Warning("[GemBasic] 材料区不是网格，已改用双网格窗口");
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Error("[GemBasic] 双网格窗口创建失败: " + e);
                }
            }

            if (window != null)
            {
                window.SetTitle("基础结晶槽");
                window.Validate();
                m.SetContentWindow(window);
            }

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
                if (slot != null) slot.tooltipName = "水槽";
            }

            Parts[m.Pointer] = new MachineParts { Main = main, Water = water };
            MelonLogger.Msg($"[GemBasic] 结晶槽 main={main?.GetType().Name} mainGrid={main?.TryCast<GameGridInventory>() != null} water={water?.GetType().Name} waterGrid={water?.TryCast<GameGridInventory>() != null}");
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
}
