# 「宝石工房 / The Gem Atelier」Mod 计划书

> 游戏：Probably Stolen Demo v0.46D（IL2CPP + MelonLoader 0.7.3）
> 主题：宝石鉴定 + 人工培育
> 状态：**仅计划，未实现**
> 日期：2026-10-13

---

## 1. 项目定位

做一个**以"未知价值"为核心的宝石产业链**：你收到的是说不清价值的原石，靠鉴定把它变成"确定的钱"，
靠培育把它变成"可量产的钱"——但培育货会被识破、天然货才值钱。

三个动词：**鉴定（Appraise）→ 切磨（Cut）→ 培育（Cultivate）**，最后落到**销售 / 镶嵌**。

设计原则：
1. **复用原版机制**，不另造一套：游戏自带鉴定公会、`ItemFeature` 真假显示、水培机器、商店小贩。
2. **先能玩，再丰富**：M0 用 ItemForge 内容包先把物品和机器跑起来，M1 再接原生鉴定。
3. **风险即乐趣**：原石可能一文不值，培育货可能被鉴定师识破——有赌注才有味。

---

## 2. 可复用的现成东西（已核实）

| 能力 | 游戏/现有 mod 里的东西 | 用在哪 |
|---|---|---|
| 物品注册 | `Directory<GameItem>.Add(id, factory)` / `DirectoryMaster.Item(id)` | 全部新物品 |
| 内容包框架 | `ItemForge`：`PSItemPack_*.dll` + `ItemForge.items.json` + 嵌入 PNG | M0 全内容 |
| 定时加工机器 | ItemForge `ProcessingMachineEnabled` + `ProcessingMachineRecipes`（输入→输出/天数） | 培育舱 |
| 手动工作台 | ItemForge `ProcessingContainerEnabled`（配料/产出/计时） | 鉴定台、切磨台 |
| 手持加工工具 | ItemForge `ItemTransformEnabled`（按重量/数量转换） | 切割刀、抛光 |
| **原生鉴定** | `AppraisalHelper.InitAppraisalItem / SetDefaultAppraisalValue / GetCorrectBaseValue / CreateAppraisalTooltip` | 原石可鉴定化 |
| **鉴定公会服务** | `AppraisalServiceHelper`（估价/真值/手续费/精度/对话）、`StoreClientListAppraisal.CreateAppraisal1/Random` | 公会鉴定 |
| **真假价值/揭示** | `ItemFeature.SetPublicDisplay / SetActualDisplay / DiscoverFeature / IsFeatureDiscovered / ExposeFeature`，`GetFakeValueModifier / GetActualValueModifier` | 未鉴定 ↔ 已鉴定 |
| 水培式培育 | `HydroponicDirectory`、`MachineHydroponic.GetInputSlot / IsSeedInSlot / CanWater / GetGrowStatus`、`PlantAnalyser` | 高级培育舱（可选） |
| 商店/小贩 | `PlayerStore.Instance.AddDirectSellingItemToTable`、`StoreClientListDict.CreateStoreClient` | 宝石商人 |
| 起始天赋 | `StartingPerk` / `StartingPerkType` / `StartingPerk.IsPerkActive`（WineAppraisalMaster 的 `PerkRegistry` 是范本） | 宝石匠开局 |
| 假货/禁品 | `ContrabandHelper.InitContrabandItem(item, level)`、`StolenHeat` | 合成宝石/假宝石 |
| 品质分级 | 参考 `WineHelper.GetWineQualityTier` 的"按内部数值分档"思路 | 宝石 1–5 档 |

范本 mod：`WineAppraisalMaster`（鉴定/真假特征/天赋）、`BrewingExpansion`（机器/小贩/盲盒/品质）、
`PSItemPack_Gofishing`（ItemForge 机器包）、`ProbablyStolenInspector`（运行期查数据）。

---

## 3. 核心玩法循环

```
                 ┌───────────── 原料 ─────────────┐
                 │ 原石（搜刮/商店/矿脉）            │
                 └───────────────┬────────────────┘
                                 ▼
         ┌──────────── 鉴定（把"未知"变成"已知"）────────────┐
         │ · 直接盲卖：低价、确定性                       │
         │ · 公会鉴定：付费 + 估价博弈，揭示种类/品质      │
         │ · 自建鉴定台（后期）：成本低但会看走眼            │
         └──────────────────┬─────────────────────────────┘
                            ▼
         ┌──────────── 切磨（提升价值）────────────┐
         │ 原石 → 裸石（切割/抛光，按重量或数量）      │
         │ 失败有损耗；工艺影响成色                    │
         └──────────────────┬─────────────────────┘
                            ▼
         ┌──────────── 培育（量产）───────────────┐
         │ 晶核(一次性) + 培养液 → 培育舱 → N 天 → 培育宝石 │
         │ 品质受晶核品级/水质成分/机器/时长/RNG 影响   │
         │ 有变异稀有款；但带"人工"标签，价值打折      │
         └──────────────────┬─────────────────────┘
                            ▼
         ┌──────────── 销售 / 镶嵌 ─────────────┐
         │ 宝石商人、奢侈品客户；戒指/吊坠提高单价      │
         │ 培育货被鉴定师识破 → 折价/声望/违禁风险     │
         └──────────────────────────────────────┘
```

一句话节奏：**前期靠鉴定赌原石，中期靠切磨提价，后期靠培育躺赚但要防识破。**

---

## 4. 内容设计

### 4.1 物品清单（id 统一 `gem_` 前缀，避免和现有包撞车）

**原料**
| id | 名称 | DirectoryKind | Types | 说明 |
|---|---|---|---|---|
| `gem_raw_common` | 普通原石 | Misc | MATERIAL, JUNK | 常见，预期价值低 |
| `gem_raw_rare` | 稀有原石 | Misc | MATERIAL, LUXURY_ITEM | 来自稀有渠道 |
| `gem_raw_volcanic` | 火山原石 | Misc | MATERIAL | 高温环境，易出红系 |
| `gem_raw_deep` | 深岩原石 | Misc | MATERIAL | 深矿，易出蓝系 |
| `gem_core` | 晶核（一次性） | Misc | MATERIAL | 培育种子，只能用一次；靠商人供货 |
| `gem_nutrient` | 培养添加剂 | Misc | CHEMICAL_SUPPLIES | 矿盐/致色剂等，分 3 档 |
| `gem_polish` | 抛光粉 | Misc | CHEMICAL_SUPPLIES | 切磨消耗品 |

**宝石本体**：4 个种类 × 5 个品质档（可先做 1 种 × 5 档验证）
- 种类：`ruby` 红宝石 / `sapphire` 蓝宝石 / `emerald` 祖母绿 / `opal` 蛋白石（或 `void` 虚空晶）
- 品质：` flawed` 瑕疵 / `plain` 普通 / `fine` 优良 / `rare` 稀有 / `perfect` 完美
- id 例：`gem_ruby_fine`；培育版加 `_cultured`，如 `gem_ruby_fine_cultured`
- 价值 = 基础价 × 种类系数 × 品质系数；培育版 ×（0.35~0.6）

**工具**
| id | 名称 | 说明 |
|---|---|---|
| `gem_loupe` | 珠宝放大镜 | 自建鉴定用；降低看走眼概率 |
| `gem_cutter` | 宝石切割刀 | ItemForge transform 工具，原石→裸石 |
| `gem_polisher` | 抛光轮 | 裸石→高品质裸石 |

**机器**
| id | 名称 | 类型 | 行为 |
|---|---|---|---|
| `gem_appraisal_bench` | 鉴定台 | ProcessingContainer（或原生服务） | 放入原石+放大镜 → 揭示 |
| `gem_cutting_bench` | 切磨台 | ProcessingContainer | 原石/裸石 + 抛光粉 → 裸石 |
| `gem_incubator` | 晶石培育舱 | ProcessingMachine | 晶核+培养液 → N 天 → 培育宝石 |
| `gem_display_case` | 宝石展示柜 | StorageContainer | 收纳/防鼠（可选） |

**后期**
- 首饰：`gem_ring` 宝石戒指、`gem_amulet` 吊坠（裸石 + 金属锭，走切磨台配方）
- 鉴定师 NPC / 宝石商人（StoreClient）
- 起始天赋：`gem_appraiser` 宝石鉴定师（鉴定精度↑、开局原石）、`gem_cultivator` 培育师

### 4.2 鉴定规则（核心）

- 原石创建时调用 `AppraisalHelper.InitAppraisalItem(item, defaultBlindValue)`；
  挂一个 `ItemFeature`：`SetPublicDisplay("原石（未鉴定）")`，真实值放 `SetActualDisplay / actualValueModifier`。
- **两种鉴定路径**：
  1. **公会鉴定**（原版服务）：付费 + `AppraisalServiceHelper.OnEstimate` 估价博弈；
     精度 `GetEstimatePrecision` 由估价与真值差距决定，可用天赋/放大镜修正。
  2. **自建鉴定台**（后期）：成本低，但按概率"看走眼"（给出错误品质档），放大镜降低误判。
- 鉴定成功后：`ItemFeature.DiscoverFeature()` / `ExposeFeature()`，公开显示变真实值，
  物品变成对应的 `gem_<种>_<档>`（或直接改显示与 `unitValue`）。
- **假的可能**：原石有概率是玻璃/染色石（`ContrabandLevel`），鉴定后价值极低。
- **培育货识破**：培育宝石带 `CULTURED` 标签；公会鉴定有概率识破，识破后：
  价值按培育打折、可能扣声望、高等级算 `CONTRABAND`。

### 4.3 培育规则

- 培育舱（ItemForge `ProcessingMachine`）：
  `gem_core_<种>`（**一次性**）+ 培养液 → N 天 → `gem_<种>_<档>_cultured`
- 品质分布由三件事决定，做成可调参数：
  1. 晶核品级（普通/稀有；没有耐久，长完即消耗）
  2. 培养液成分（水质纯度 + 矿物/重金属/酸碱；见培育系统设计）
  3. 机器与培育时长（模组提高上限，越久越可能高品质，也有失败/变异的尾巴）
- **变异**：低概率产出稀有颜色变种（如"星芒红宝石"），价值高于普通培育货，但仍带 `CULTURED`。
- 可选进阶：克隆水培机器（`MachineHydroponic`），做"浇水/生长状态"的慢节奏版本，
  用 `GetGrowStatus` 显示进度——更像"培育"而不是"定时器"。

---

## 5. 技术方案（分阶段）

### M0 — ItemForge 内容包（1–2 天）
**产出**：`ItemForgePackTemplate` 改造成 `PSItemPack_Gem.dll`。
- 全部基础物品 + 鉴定台/切磨台/培育舱（用 ItemForge 现成字段）。
- 每件物品嵌入 PNG 占位图标。
- 验证：物品能在游戏里注册、图标显示、机器能放料按天产出。
- 限制：ItemForge **不支持**真假显示/原生鉴定，M0 的"鉴定"先用"容器配方"近似。

### M1 — 原生鉴定接入（3–5 天）
**产出**：独立 `GemAtelier.dll`（MelonLoader mod，引用 `Il2CppAssemblies`）。
- Harmony patch：`AppraisalHelper.InitAppraisalItem` 给原石加鉴定信息；
  `ItemFeature.GetPublicDisplay/GetActualDisplay` 按鉴定状态返回不同文本；
  `AppraisalServiceHelper` 挂自己的估价/精度/手续费规则。
- 可选：注册 `StoreClientListAppraisal` 风格的宝石鉴定师客户。
- 鉴定台 UI：优先套用 ItemForge 的 ProcessingContainer，减少自绘 UI。
- 参考 `WineAppraisalMaster` 的 Prohibition 补丁写法（prefix 改显示、ExposeFeature）。

### M2 — 培育深度（3–5 天）
- 品质/变异/失败表；机器模组；培养液成分档位。
- 识破机制：公会鉴定对 `CULTURED` 标签按概率揭穿。
- 经济：`PriceHelper` 按重量/品质定价，培育折扣。

### M3 — 销售 / NPC / 天赋（3–5 天）
- 宝石商人 `StoreClient`（或把货铺到现有奢侈品小贩）。
- 首饰配方；奢侈品客户偏好。
- 起始天赋（`StartingPerk`）、每日/每周事件（可选）。

### M4 — 打磨（2–3 天）
- 图标、文案、平衡、存档兼容、README、反安装说明。

> 工期是单人的粗略估计；每个里程碑都能独立交付和试玩。

---

## 6. 关键技术映射表

| 需求 | 落点 |
|---|---|
| 新物品注册 | `((Directory<GameItem>)dir).Add(id, Func<GameItem>.op_Implicit(factory))` |
| 造物品 | `ItemDirectory.CreateEmptyItem(id)` + `identifier/name/shape/unitValue/itemTypes` |
| 按 id 取物品 | `DirectoryMaster.Item(id, true)` |
| 机器（定时） | ItemForge `ProcessingMachineEnabled` + `ProcessingMachineRecipes` |
| 工作台（手动） | ItemForge `ProcessingContainerEnabled` + `ProcessingContainerRecipes` |
| 工具 | ItemForge `ItemTransformEnabled` + `ItemTransformRecipes` |
| 可鉴定标记 | `AppraisalHelper.InitAppraisalItem(item, value)` |
| 假/真显示 | `ItemFeature.SetPublicDisplay / SetActualDisplay` |
| 揭示 | `DiscoverFeature / ExposeFeature / IsFeatureDiscovered` |
| 公允价值 | `AppraisalHelper.GetCorrectBaseValue(item)` |
| 公会估价 | `AppraisalServiceHelper.OnEstimate / IsAppraisalAcceptable / GiveAppraisalFee` |
| 水培式生长 | `MachineHydroponic.GetInputSlot / CanWater / GetGrowStatus` |
| 小贩上架 | `PlayerStore.Instance.AddDirectSellingItemToTable(...)` |
| 自定义小贩 | patch `StoreClientListDict.CreateStoreClient` |
| 违禁/识破 | `ContrabandHelper.InitContrabandItem(item, level)` |
| 天赋 | `StartingPerk` + `StartingPerk.IsPerkActive` |

⚠️ 注意：`Assembly-CSharp.dll` 是 IL2CPP 互操作桩，**签名可信、方法体不可读**。
原生鉴定的具体内部流程需要运行期用 `ProbablyStolenInspector` 或日志实测确认。

---

## 7. 测试计划

1. **注册测试**（M0）：启动游戏 → 日志确认包加载 → 新物品出现、图标正常、无报错。
2. **机器测试**（M0）：放料 → 天数推进 → 产出正确；跨存档读档不丢状态。
3. **鉴定测试**（M1）：未鉴定 → 公会鉴定 → 显示与价值变化正确；看走眼分支可复现。
4. **经济测试**：同品质天然/培育价值差合理；批量培育不破坏经济。
5. **兼容性**：与现有 `BrewingExpansion / PSProcurement / WineAppraisalMaster` 同开无冲突（日志查 Harmony 冲突）。
6. **反安装**：删 DLL 后旧存档不崩（或给出迁移说明）。

---

## 8. 风险与取舍

| 风险 | 影响 | 对策 |
|---|---|---|
| ItemForge 无真假显示 | M0 鉴定只能近似 | M1 上独立 patch |
| 原生鉴定流程不透明 | 可能接错点 | 先小范围实测，日志驱动 |
| 物品过多 | 图标/平衡成本高 | 先 1 种宝石 × 5 档，验证后再扩 |
| 培育破坏经济 | 数值崩 | 培育折扣 + 识破/违禁 + 限产 |
| 存档耦合 | 删 mod 坏档 | 物品 id 可安全消失；关键状态只存 mod 侧 |
| 与酒鉴定 mod 冲突 | 同时改 `ItemFeature` | 各自 prefix 不同 tag，读取时判 tag |

**范围控制建议**：第一版只做 **1 种宝石（红宝石）× 5 品质 + 原石 + 鉴定 + 培育舱 + 1 个商人**，
跑通闭环后再复制到其他宝石和首饰。

---

## 9. 需要你拍板的点

1. **鉴定形态**：走原版"鉴定公会服务"（贴合世界观、但受原版流程限制），还是自建"鉴定台 + 看走眼概率"（自由度高）？→ 建议 **两者都做，公会为主、鉴定台为后期解锁**。
2. **培育节奏**：ItemForge 定时转化（简单、1 周能跑），还是克隆水培做"浇水/生长状态"（更像培育、成本高）？→ 建议 **先定时，M2 再升级**。
3. **规模**：先 1 种宝石验证，还是直接 4 种？→ 建议 **1 种起步**。
4. **要不要"假宝石/欺诈"线**：会牵扯违禁与声望，味道足但平衡难。→ 建议 **M2 再加**。
5. **要不要首饰/镶嵌**：能显著提高后期深度，但需要新的配方与图标。→ 建议 **M3 视情况**。
6. **语言**：中文单语（直接写中文名）还是中英双语（`EnglishName` 也填）？→ 建议 **双语都填**，成本很低。

---

## 10. 建议的下一步

确认上面第 9 节的选择后，我可以：
- 把 `ItemForgePackTemplate` 改造成 `PSItemPack_Gem`（M0），先给你一个能进游戏、能培育的版本；
- 再写 `GemAtelier` patch mod 接原生鉴定（M1）。

如果你没有特别偏好，我将按计划书里的"建议"默认值推进。
