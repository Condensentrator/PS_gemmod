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

### 品质（v0.5）

- 每次成功生长时，读取培养液的非致色杂质：
  - 杂质 = `PLANT_MATTER + ORGANIC_WASTE`；
  - 分母 = 杂质 + `HEAVY_METAL + MINERAL + CHEMICAL_CONTAMINANT`；
  - 即致色成分和水不参与杂质计算。
- 品质按历史平均值计算（`GEM_IMP_SUM` / `GEM_IMP_COUNT`），不是只看最后一次水质。
- 5 个常规级别：
  - 无瑕级：净度 ≥90%，价格 ×2.25（+125%）
  - 极优级：75%–90%，×1.75（+75%）
  - 优良级：55%–75%，×1.00（无修正）
  - 普通级：35%–55%，×0.55（-45%）
  - 劣质级：<35%，×0.25（-75%）
- 特殊级别“包裹体级”：平均杂质 ≥30% 且至少生长过 2 次，每次生长有 28% 概率触发；一旦触发固定保留，价格 ×3.00（+200%），是价格加成最高的级别。
- 名称后缀 `· 品质名`；描述显示标签品质、价值修正；标签与真实一致时才显示净度/平均杂质。
- **已接入原版标签体系**：
  - `ItemFeature` 分类 `item_feature_category_gem_quality`，6 个 `ItemCondition`（劣质/普通/优良/极优/无瑕/包裹体）；
  - 标签打印机（`LabelerHelper` / `LabelerElement`）可以直接改宝石的 fake condition；
  - 价格不再乘在 `unitValue` 上，而是由原版 `ItemFeature` 价值修正计算；
  - 生长只更新真实品质，玩家手动贴过的标签保留（`GEM_LABEL_CUSTOM`）。
- **假标签后果**：
  - 卖出时 `SoldItem.TrackItem` 记录标签高于真实的宝石（用 `fakeCondition`，即使当场被揭穿也算）；
  - `PlayerStore.EndNight` 结算 `SecData.CommitCrime("sec_data_crime_fraud", soldPrice)`，证据类型为原版“欺诈性虚假陈述”；
  - 只有带 `GEM_JEWELER_DETECT` 标签的买家（上层区珠宝商）会排队次日退款；
  - 退款用 `ChemicalFeedbackHelper.QueueFeedbackClient` + 直接传入中文退款文本，不再复用成瘾品退货文本；
  - 原版 `ClientExposeFeature` 会调用自定义 feature 不存在的 `clientExposeDialog`，导致 NRE/卡死；
    现在用 Harmony prefix 接管：上层手动 `ExposeFeature`，中层/下层直接跳过，所有培育宝石都不走原版揭穿路径。

### 供货与收购（v0.6）

- 晶核霓虹灯牌（`gem_sign`）：
  - 初始发放 1 块，博士商店也会补货；
  - 放进展示柜后，开门时由 `GemSupply` 检测（`ShowcaseHelper.IsShowcaseContainItemById` + 展示柜回调标记兜底）；
  - 每次开门最多生成 1 个“晶核供货商”，当天固定带来 1 台结晶槽、5 枚通用晶核、2 枚红宝石晶核、4 枚绿柱石晶核、4 枚石英晶核；
  - 供货商从原版 `StoreClientList.CreateSupplier()` 生成，保留原版供货/批发流程，只替换身份和主对话；
  - 库存与原版/CharacterForge 一致：顾客到柜 (`StoreClientMono.OnArrived`) 后，用 `PlayerStore.AddDirectSellingItemToTable(item, isOwend:false)` 直接摆到销售桌上；
  - 用 `PlayerStore.AddDirectSellingItemToTable` prefix 拦掉原版给该供货商自动摆的模板货，避免和晶核混在一起；
  - 展示柜预测面板追加 `("晶核供货", 400, "晶核供货商")`。
- 杰克逊博士商店固定库存：
  - 挂 `TraderFactory.CreateTrader` / `CreateDoctor` postfix，以及 `BarterOffer.InstanciateOffer` 兜底；
  - 直接往博士的 `barterInventory`（fallback `barterBuyInventory`）或当前 barter 客户的 `gridInv` 里补基础结晶槽、刚玉/绿柱石/石英晶核和灯牌；
  - 没有概率，每次开店都会补缺；日志会输出创建到的商人 id/name 和库存是否为空，便于定位。
- 展示柜宝石追踪（`GemShowcase`）：
  - `PlayerStore.AddedItemToDisplayCase` / `RemovedItemToDisplayCase` 和 `GameItem.OnAddedToShowcaseArea` / `OnAddedToWeightedArea` / `OnAddedToClientArea` 共同维护；
  - 用 `GEM_IN_SHOWCASE` 标签做旧档重建，`BeginDay` 时重新扫描；
  - 总价值用 `GameItem.GetCurrentValue()` 计算，不再按尺寸估算。
- 原版展示柜预测：
  - `ShowcaseHelper.GetDisplayCasePrediction` postfix 追加 `("宝石", 总价值, "珠宝商")`；
  - `ShowcaseHelper.GetDisplayCaseType` postfix 把“宝石”从原版客户生成类型里移除，避免调用未知的 `CreateClientByStockType("宝石")`；
  - `HandleDisplayCase` postfix 按总价值分三档追加客户：
    - ≥2000：上层区珠宝商，带手持光谱仪，可 `ExposeFeature` 识别假标签，预算 ×3；
    - 800–1999：中层区珠宝商，不识别真伪，预算 ×2；
    - <800：下层区饰品贩子，不识别真伪，预算 ×1.5；
  - 三档都有自定义主对话文本，只收 `GEM_CULTURED`；中层/下层用“永远返回 false”的 exposure 谓词保证不会揭穿。

### 水质

- 直接读容器 `CURRENT_PART_*` 标签（1000 份 = 1 ml）：
  - `HEAVY_METAL`、`MINERAL`、`CHEMICAL_CONTAMINANT` → 决定颜色（按无机比例，过半优先，不过半按前两项混合）；
  - `PLANT_MATTER`、`ORGANIC_WASTE` → 纯杂质，只降低生长速度（最多 -60%）。
- 培养液必须放在专用水槽，每次成功生长扣 100 ml。
- 纯度来自 `WaterHelper.GetWaterPurity`，原始刻度 0–10000（9837 = 98.37%）。

### 机器

- 自建 `gem_growth_basic`：直接使用原版水培机的 `DirectoryUtils.CreateHydroponicSystemWindow(5, 5)`，一次生成材料区、水槽和 5×7 模组区；
- 专用水槽：`AllowOnlyTaggedItemOwn(..., "LIQUID_CONTAINER_TAG", ...)`，空槽占位 4×4；
- 5×5 材料区：`AllowOnlyTaggedItemOwn(..., "GEM_ITEM", ...)`；
- 5×7 模组区：由水培机窗口自带，按 `GameSlotInventory` / `GameGridInventory` 尺寸区分材料区、水槽和模组区；
  - 质量（`BONUS_PERCENTAGE_QUALITY_INT`）→ 晶核变宝石时的初始重量；
  - 性能（`BONUS_PERCENTAGE_PERFORMANCE_INT`）→ 每日重量增长率；
  - 效率（`BONUS_PERCENTAGE_EFFICIENCY_INT`）→ 每次生长耗水（100 / (1 + 效率/100)）；
- 右侧 2×2 备注栏：`CreateInventoryWindow(2,2)` + `PreBuiltItemHelper.CreateHandnote` 放入说明纸片；
- 用 `GameGridInventory.TryFindOneValidInventorySlot` 放置，避免宝石叠在一格；
- 输入输出共用材料区，成品优先放回原区域。

### 类型本地化

- 游戏真实键：`type_gemstone`，表名 `Mechanic`；
- 通过 `LocalizationSettings.StringDatabase.GetTable("Mechanic", locale).AddEntry("type_gemstone", "宝石")` 注入；
- 对所有可用语言注入（中文"宝石"，其他 "Gemstone"）。

### 发放

- 默认发放一套培育材料：最多 3 台结晶槽 / 8 枚通用晶核 / 4 枚绿柱石晶核 / 4 枚石英晶核 / 6 瓶测试水 / 1 个标签打印机；
- 测试水带 `GEM_TEST_WATER` 标记，旧档里已有测试水时不再重复补；
- 旧档里之前发过的 `gem_beryl_aqua_showcase` 成品展示会在加载时自动移除；
- 一次发太多柜台可能放不下，必要时再调低 `GrantBulk` 的数量。

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
7. **NPC 真伪鉴定**：原版用 `ItemFeature` 同时挂 `realCondition` 和 `fakeCondition`，未暴露时用 fake 报价；顾客带扫描工具时 `CanExposeFeature()` → `ExposeFeature()` 切换为 real 并触发对话/降价。梦尘、水质、香烟、食品券、免疫宁都走这一套。详见 `docs/NPC鉴定与真伪系统研究.md`。

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
