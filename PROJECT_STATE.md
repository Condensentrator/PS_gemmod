# 项目状态 / 上下文摘要

> 这份文件用于压缩对话上下文：记录已经做成了什么、关键技术结论、当前代码状态和下一步。

## 1. 项目概况

- 游戏：Probably Stolen Demo v0.46D（IL2CPP + MelonLoader 0.7.3，net6）
- 框架：ItemForge（`Mods/ItemForgeRuntime.dll`，Runtime API 20）
- 本项目：`PSItemPack_GemBasic.dll`（ItemForge 内容包 + Harmony 补丁，同一 DLL）
- 核心闭环：`晶核 + 培养液 → 专用水槽 → 按天生長 → 重量/阶段 → 出售`

## 2. 已实现

### 物品

- 矿物：
  - 刚玉 5 色：红/蓝/绿/黄/粉；
  - 绿柱石 4 色：祖母绿/海蓝/摩根石/金绿柱石；
  - 石英 1 色：紫水晶。
- 每种矿物 × 颜色 × 4 阶段（小/中/大/巨），各自独立 item id 和预染色图标。
- 晶核：`gem_core`、`gem_core_ruby`、`gem_core_beryl`、`gem_core_quartz`，全部白模，形状不同。
- 成品类型：`LUXURY_ITEM` + `GEMSTONE`（`MATERIAL` 已从成品移除）。
- 晶核类型：`MATERIAL`。

### 生长

- 晶核 → 小宝石：2 天；宝石继续生长：每天一次。
- 初始重量 30–70 g；每天增长 `4%`（纯度 ≥70% 时 6%，<30% 时 2%）。
- 重量阈值 80/200/400 g → 中/大/巨；上限 600 g。
- 原料一次消耗：晶核毁灭，产出一颗宝石。
- 普通晶核按水质决定矿物颜色；`gem_core_ruby` 固定红；石英固定紫水晶。

### 水质

- 直接读容器 `CURRENT_PART_*` 标签（1000 份 = 1 ml）：
  - `HEAVY_METAL`、`MINERAL`、`CHEMICAL_CONTAMINANT` → 决定颜色（按无机比例，过半优先，不过半按前两项混合）；
  - `PLANT_MATTER`、`ORGANIC_WASTE` → 纯杂质，只降低生长速度（最多 -60%）。
- 培养液必须放在专用水槽，每次成功生长扣 100 ml。
- 纯度来自 `WaterHelper.GetWaterPurity`，原始刻度 0–10000（9837 = 98.37%）。

### 机器

- 自建 `gem_growth_basic`：`DirectoryUtils.CreateDoubleInventoryWindowSlotInput` 窗口；
- 专用水槽：`AllowOnlyTaggedItemOwn(..., "LIQUID_CONTAINER_TAG", ...)`；
- 5×5 材料区：`AllowOnlyTaggedItemOwn(..., "GEM_ITEM", ...)`；
- 用 `GameGridInventory.TryFindOneValidInventorySlot` 放置，避免宝石叠在一格；
- 输入输出共用材料区，成品优先放回原区域。

### 类型本地化

- 游戏真实键：`type_gemstone`，表名 `Mechanic`；
- 通过 `LocalizationSettings.StringDatabase.GetTable("Mechanic", locale).AddEntry("type_gemstone", "宝石")` 注入；
- 对所有可用语言注入（中文"宝石"，其他 "Gemstone"）。

### 发放

- 默认只发一个 `gem_beryl_aqua_showcase`（海蓝宝石成品展示，2×2）；
- 批量发放（3 台机器 / 8 刚玉核 / 4 绿柱石核 / 4 石英核 / 6 瓶测试水）在 `GrantBulk()`，默认注释掉；
- 一次发太多柜台放不下。

## 3. 关键技术结论（踩坑记录）

1. **ItemForge 内容包**：`PSItemPack_*.dll` + 嵌入 `ItemForge.items.json` + `ItemForge.icons.<id>.png`。
2. **运行时染色不可靠**：
   - `ItemSpriteModifier` 是叠加精灵，不是染色；
   - 纹理乘色 + 取图补丁在物品 UI 上不生效；
   - `GameItemElement.Validate` + `Image.color` + `ToggleBackground` 对测试核有效，但正式物品刷新会重置。
   - **最终方案：预染色贴图 + 按颜色拆 item id**，稳定且无性能开销。
3. **类型标签是字符串**：显示键由类型名转小写并加 `type_` 前缀（`GEMSTONE` → `type_gemstone`），表 `Mechanic`。
4. **水培机是占位符**，不能克隆；机器窗口需自建。
5. **商店启动期 NRE**：不要在 `OnUpdate` 里过早访问 `PlayerStore.Instance`，会触发商店构造并在 UI 节点上 NRE。
6. **网格放置**：要用 `GameGridInventory.TryFindOneValidInventorySlot`，基类 `TryInventorySlot` 会返回 `index=-1`，导致物品叠在一起。

## 4. 下一步（路线）

- P2：扩更多矿物/颜色（石英黄玉、尖晶石等）——已具备"填表 + 预染色图标"管线；
- P3：水质化学加深（掺杂剂、酸碱、稳定剂/还原剂）；
- P4：高压舱/温控/催化/退火模组（2 台机器 + 模组）；
- P5：宝石商人、鉴定师、委托、拍卖；
- P6：原石/鉴定/切割；
- P7：首饰/图鉴/成就/限定色事件。

## 5. 仓库结构

```
src/        当前内容包与补丁
docs/       设计、玩法、培育、扩展路线文档
examples/   最小 ItemForge 内容包模板
```
