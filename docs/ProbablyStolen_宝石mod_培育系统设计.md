# 宝石培育系统设计（晶核 · 水质 · 陈化）

> 配套：`ProbablyStolen_宝石mod_计划书.md` / `ProbablyStolen_宝石mod_玩法实现设计.md`
> 目标：把"人工培育"做成有深度、和原版系统咬合的玩法，而不是一个定时转换器。
> 参考原版：**养老鼠（遗传/生长/卫生/拥挤）**、**酿酒（固定发酵 + 陈化增值）**、**水质（纯度/污染物）**。

---

## 0. 一句话概念

> **晶核是种子，水是培养基，矿盐是肥料，时间是成本，退火是陈化。**

现实里培育宝石就是在过饱和溶液里让晶核慢慢长（水热法/助熔剂法）。把它翻译成本游戏的语言：
**水质决定成色，矿盐决定种类与速度，时间决定风险，陈化决定溢价。**

---

## 1. 和原版三套系统的对应关系

| 原版 | 它的机制 | 宝石培育里的对应 |
|---|---|---|
| **养老鼠** | 生长阶段、饥饿、卫生、拥挤、疾病、生育遗传、老化 | 生长**进度**、槽内**拥挤减益**、水质**污染累积** |
| **酿酒** | 固定发酵时间 + 额外陈化增值 + 水质决定品质 | 固定**生长天数** + **退火/稳定化**额外增值 + 水质决定品质 |
| **水质** | 纯净/优质/标准/幽水/锈水/沟渠多档，纯度数值，可检测/可伪造 | 水的**纯度数值**直接进品质公式；**污染物类型**决定色彩与变异 |

这样玩家不需要学一套新逻辑，直觉是通的：**水越干净，晶越好；放得越久，风险越高。**

---

## 2. 培育槽（核心机器）

**`gem_growth_vat` 晶石培育槽**（容器类机器，放料后按天生长）

### 槽位

| 槽 | 内容 | 说明 |
|---|---|---|
| 晶核槽 | `gem_core_<种>` | 一次性种子；有种类/品级 |
| 水槽 | 任意水容器 | 读水量 + 纯度 + 污染物 |
| 矿盐槽 | `gem_dopant_*` | 决定种类偏向与生长速度 |
| 成品槽 | — | 收获时产出 |

### 一次培育的流程

```
放晶核 → 注水 → 加矿盐 → 选模式 → 固定生长 N 天 → 收获 →（可选）退火 M 天
```

**生长模式**（对应现实"快长 vs 慢长"）：

| 模式 | 速度 | 品质倾向 | 风险 | 水耗 |
|---|---|---|---|---|
| **速生** | ×1.6 | 低（杂质多） | 裂晶率高 | 高（污染快） |
| **标准** | ×1.0 | 中 | 中 | 中 |
| **精养** | ×0.5 | 高（杂质少） | 低 | 低但要求纯度高 |

> 这是整个系统最重要的取舍：**要产量还是要完美**。速生适合铺货，精养适合冲完美/稀有变异。

---

## 3. 水质怎么真正参与

### 3.1 纯度 → 品质

游戏里 `WaterHelper.GetWaterPurity(item)` 直接给出纯度数值（0–100 量级），
`WaterFeatureHelper` 里已有纯净/优质/标准/幽水/锈水/沟渠多档显示。
培育时：

```
inclusionChance = baseInclusion - purity * k + speedPenalty + crowdingPenalty
```

- 纯度越高，**内含物/裂隙**越少，越可能出 `fine/rare/perfect`；
- 纯度低不直接"失败"，而是把品质往下压——玩家能算得过账，但心疼。

### 3.2 污染物 → 色彩与变异（关键风味）

污染物不是纯负面，**不同水会"掺杂"出不同颜色**，这才是水质系统有意思的地方：

| 水（原版档位） | 生长速度 | 品质 | 色彩偏向 | 特殊产物 |
|---|---|---|---|---|
| 纯净水 | 慢 | 最高 | 无 | 无 |
| 优质水 | 中 | 高 | 无 | 无 |
| 标准水 | 基准 | 中 | 无 | 无 |
| 锈水 | +30% | 低 | 红/橙 | 赤红变种（ruby 溢价） |
| 幽水 | 慢、波动大 | 中但方差大 | 蓝/紫 | 虹彩/蛋白石变种（opal 溢价） |
| 沟渠水 | +60% | 很低 | 绿/黄 | 高裂晶率、只出低档 |

> 设计意图：**便宜水不是不能玩，而是玩的是"赌稀有颜色"**。后期用净化水稳定出高品质，
> 但想要某些限定色，反而得故意用"脏水"。这给水质系统一个反向用途。

### 3.3 水量 → 颗粒数 vs 单颗重量

- **稀释培养**（水多、矿盐少）：一炉出 **多颗小宝石**（数量多、单颗重量小、单价低）。
- **浓缩培养**（水少、矿盐多）：出 **一颗大宝石**（重量大、单价高）。
- 实现上直接对应 `GeneralHelper.InitWeight` + `PriceHelper.GetWeightedPrice`，
  和 ItemForge 的 `UseInputWeight` 思路一致。
- 水量在生长期间**逐日消耗/污染上升**：中途可以换水/过滤（这是滤水器的又一个消耗点）。

### 3.4 维护：污染累积（承接"卫生"）

仿养老鼠的卫生值：
- 培育槽每天累积"污染度"，纯度越低累积越快；
- 污染度超过阈值 → 品质惩罚 + 裂晶概率上升；
- 玩家可以中途**换水**或放**滤水器**清污染；
- 收获后槽里剩"废水"（低纯度水/晶渣），需要倒掉或净化——形成水的循环。

---

## 4. 固定生长时间 + 陈化（承接酿酒节奏）

### 4.1 生长（对应"发酵"）

每种宝石固定基础天数（示例）：

| 种类 | 基础天数 | 备注 |
|---|---|---|
| 蛋白石 | 3 | 快，但品质方差大 |
| 红宝石 | 4 | 中 |
| 蓝宝石 | 5 | 中 |
| 祖母绿 | 6 | 慢，高品质上限高 |

实际天数 = 基础 × 模式系数 ÷ 晶核生长特性 ÷ 矿盐加成。
**进度存在机器的 tag 上**（`GEM_GROW_DAY`/`GEM_GROW_NEED`），读档不丢。

### 4.2 退火 / 稳定化（对应"陈化"）

培育宝石出槽时带 `GEM_UNSTABLE`；放进 **退火炉/展示柜** 再放 M 天：

- 每天有概率"稳定化"一级：`plain → fine → rare`（有上限，不能白嫖到 perfect）；
- 稳定化提升 `unitValue`（×1.15 / 级），并去掉"培育"带来的品质惩罚；
- **风险**：水质差/生长快的宝石在退火时有概率**裂晶**（产出晶渣）；
- 这一步让"快速铺货 → 退火提价"和"精养直出 → 省时间"形成两条经济路线。

> 名字建议用"退火 / 稳定化"而不是"陈化"——宝石不老化，但内应力释放、火彩变好是合理的。

---

## 5. 晶核：一次性种子，只能靠商人补货

> 关键修正：晶核**不是可重复使用的母晶**，而是像原版作物种子一样——**一次培育消耗掉，
> 长成宝石就没了**。原版作物明确写了"无籽"，不能自留种，宝石培育也照这个经济来。

### 5.1 晶核 = 一次性种子

- 一次培育消耗 **1 枚晶核**，产出 **1 颗培育宝石**（或按水量分成一炉小晶，见 §3.3）；
- 不设耐久、不设"养晶"、不设子晶核、不设遗传/选育；
- 想吃这碗饭，就必须持续进货——这正是原版的循环：**买种子 → 种 → 卖**。

### 5.2 晶核从哪来（只能买）

- **宝石商人 / 晶核贩子上门卖**：和原版种子商、酿酒材料商一样，定期带来一批晶核；
- 库存有限、按品级定价；高级晶核（高品级/稀有种类）不是每次都有；
- **不从培育产物里回收**，也不从切原石里白嫖——否则就变成无限滚雪球，
  和"作物无籽"的原版经济矛盾。

> 如果以后试玩觉得供给太紧，可以加一个"极小概率从切原石掉晶核"的滴灌，
> 但默认不开；第一版严格走"商人供货"。

### 5.3 晶核属性（尽量少）

| 属性 | 作用 |
|---|---|
| 种类 | ruby/sapphire/emerald/opal |
| 品级 | 决定产出品质的**范围/下限**（普通核出不了完美） |
| （可选）晶型 | 轻微偏向某种颜色/包裹体，但不遗传、不迭代 |

第一版只做"种类 + 品级"。没有耐久、没有特性遗传，玩家不需要管理"母核",
只需管理"这一炉的溶液和环境"。

### 5.4 为什么这样更好

1. **经济闭环干净**：进货成本 → 培育成本 → 成品售价，利润可算，不会滚雪球；
2. **和原版一致**：玩家已经习惯"作物无籽、只能买种子"，不会觉得别扭；
3. **重点回到酿造式玩法**：乐趣在配溶液、调机器、控风险，而不是养母核；
4. **商人有存在意义**：晶核贩子是稳定的金币回收口。

---

## 6. 完整数值示例（一炉红宝石）

```
输入：晶核(red, 普通品级) + 标准水 800ml(纯度 62) + 铁矿盐 ×2
模式：标准
基础天数 4；矿盐 -1 天 → 3 天
品质判定：
  base = 1 (普通晶核)
  + 纯度修正 62 → +0
  + 矿盐 +1
  + 随机 -1..+1
  → 收束到 0..4
结果：gem_ruby_fine_cultured（举例）
消耗：晶核 ×1（一次性，长成宝石）、水（蒸发/污染）
副产：废水（纯度 30）
退火：3 天后 60% 稳定一级 → gem_ruby_rare_cultured，价值 ×1.15
```

---

## 7. 和"原石切割"的闭环

```
NPC 卖原石 ──▶ 切割 ──▶ 随机宝石 ──▶ 直接卖 / 切磨提价
                                          
NPC 卖晶核 ──▶ 晶核 + 水 + 矿盐 ──▶ 培育 ——▶ 培育宝石 ——▶ 退火 ——▶ 卖
```

**两条独立的进货线**：原石是"快钱+赌"，晶核是"慢钱+稳产"。
两者都从商人处进货，消耗金币；培育不会凭空产回晶核，所以不存在无限循环。
晶核的品质/供货量决定培育的天花板，原石的品质决定切割线的波动。

---

## 8. 分阶段落地

| 阶段 | 内容 | 技术 |
|---|---|---|
| **P1** | 培育槽：晶核 + 水 + 矿盐 → 固定天数 → 固定品质；水质只影响品质档 | ItemForge 加工机器（近似）+ 自定义 `EndNight` tick |
| **P2** | 水质映射（纯度/污染物/色彩/变异）、速生/精养/标准、污染累积、废水 | 自定义 `EndNight`；`WaterHelper.GetWaterPurity` 读水 |
| **P3** | 退火/稳定化、裂晶、重量/数量分档 | 自定义 tick + `PriceHelper` |
| **P4** | 晶核品级/商人轮换、稀有变种、模组扩展 | 自定义系统 |

> P1 甚至可以先用 ItemForge 的 `ProcessingMachine` 做"固定配方"版本，验证"放料→天数→产出"的手感；
> P2 起必须自定义 tick，因为要读水质和做随机。

---

## 9. 需要实测/决定的点

1. **水的读取与消耗**：`WaterHelper.GetWaterPurity` / `GetCurrentCapacityML` 已验证；
   但"按 ml 扣水、改纯度、生成废水"的具体写入方式要实测（可先简化成"消耗整桶水"）。
2. **水质档位与纯净度数值的映射**：`WaterFeatureHelper.WATER_PURITIES` 在运行期读出来确认阈值。
3. **培育槽是否需要供电**：建议**不需要**（降低门槛），但"精养"模式可要求供电/恒温。
4. **晶核供货**：商人的库存、价格、轮换频率是核心平衡旋钮（默认只靠商人，不白嫖）。
5. **培养规模**：一个槽一炉，还是槽内多格并行？建议第一版一槽一炉，后期再扩。

---

## 10. 推荐的第一版

**做 P1 + 水质只影响品质档**，具体：
- 晶核（4 种）+ 培育槽 + 矿盐（4 种）+ 固定 3–6 天 + 品质由晶核品级和随机决定；
- 水槽只读纯度，纯度 >60 加一档、<30 减一档；
- 晶核一次性消耗，由商人定期供货；
- 不做污染物色彩/退火/模组。

这一版一周内能玩到"买晶核→配液→培育→卖"的闭环，之后再往上加水质风味和模组。

---

## 11. 水的具体成分：可以直读（已从存档证实）

### 11.1 存储模型

水容器在 `GameItem.state`（`TagSystem`）里按 **成分槽** 存一组 `CURRENT_PART_*` 标签，
每个标签是一个 `TagState`，数值在 `valueInt` / `valueFloat` / `valueDouble` 里：

```
valueInt = 成分量（"份"，1000 份 = 1 ml）
```

实测例（30,000 ml 的清水箱）：
```
LIQUID_CONTAINER_CAPACITY = 30,000,000
CURRENT_PART_WATER               = 10,841,088
CURRENT_PART_HEAVY_METAL         =     23,743
CURRENT_PART_CHEMICAL_CONTAMINANT=     34,573
CURRENT_PART_ORGANIC_WASTE       =         18
CURRENT_PART_PHYSICAL_CONTAMINANT=         12
CURRENT_PART_MINERAL             =         16
```
纯度 ≈ `WATER / 所有成分之和` ≈ 99.46%，和 `WaterHelper.GetWaterPurity` 一致。

### 11.2 成分词表（存档里实际存在的槽位）

```
WATER              HEAVY_METAL        ORGANIC_WASTE      MICROPLASTIC
MICROBE            CHEMICAL_CONTAMINANT  PHYSICAL_CONTAMINANT  MINERAL
DREAM_CAP_MUSH     DREAM_CAP_EXTRACT  PLANT_MATTER       ALCOHOL
CAFFEINE           PROTEIN            FAT                STARCH
SUGAR              COFFEE_MUSH        CONTRACEPTIVE      ZYANIDE
NIGHTMARE_EXTRACT  NIGHTMARE_CAP_MUSH CHLORINE           SYNAPREST
FANTA              METHYLOL           REDUCING_AGENT     ACID
BASE               STABILIZING_AGENT  DEBRIS             LIPOBOND_AGENT
LIVESTOCK_STEROID  LIVESTOCK_IMMUNITY WINE_EXTRACT
```
（前缀统一 `CURRENT_PART_`；另有 `LAST_SCAN_RESULT_TAG` 存最近一次检测结果。）

### 11.3 读取代码

```csharp
// 逐成分读
var st = water.state;                               // TagSystem
foreach (var kv in st.dict) {                       // Dictionary<string,TagState>
    if (!kv.Key.StartsWith("CURRENT_PART_")) continue;
    int parts = kv.Value.GetInt();                  // 1000 份 = 1 ml
    string part = kv.Key;
}

// 单独读某一项
int mineral = water.state.GetTag("CURRENT_PART_MINERAL")?.GetInt() ?? 0;

// 汇总值
int purity = WaterHelper.GetWaterPurity(water);
int ml     = WaterHelper.GetCurrentCapacityML(water);

// 成分定义（颜色/毒性/能否被过滤等）
var liq = Liquid.GetLiquidFromId("某液体id");
// liq.displayName / liq.part / liq.currentPart / liq.color / liq.lethality
```

> `TagSystem.dict` 是 IL2CPP 的 `Dictionary<string,TagState>`，BrewingExpansion 已经用它做过
> 运行期 dump（`[BE-BAT-TAG] state.dict 共 N 个 tag`），所以这条路是通的。

### 11.4 写入/消耗成分

```csharp
WaterHelper.AddLiquid(container, liquidId, addAmount);                       // 加成分
WaterHelper.RemoveContaminantFromContainer(container, id, vol, minPercent);   // 去成分
WaterHelper.Remove(container, volumeInPart);                                 // 按份扣除
WaterHelper.PurifyToBaseWater(container);                                    // 净化
WaterHelper.SetLastScanResult(container);                                    // 刷新检测结果
WaterPremadeHelper.AccurateRustWater() / AccurateGhostWater() / ...          // 生成标准水
```

### 11.5 对培育玩法的升级：从"看纯度"变成"配培养液"

既然能读**每一种成分**，培育就不该只看一个纯度数值，而应该变成**调配培养液**：

| 成分 | 在培育里的作用 |
|---|---|
| `MINERAL` | **主养分**：决定产量/颗粒大小；生长中被消耗 |
| `HEAVY_METAL` | **致色离子**：偏红/蓝（配酸/碱决定色相） |
| `ACID` / `BASE` | **pH**：影响生长速度与缺陷率；极端值裂晶 |
| `STABILIZING_AGENT` | **稳定剂**：降低裂晶率 |
| `REDUCING_AGENT` | **还原剂**：提高色饱和度（品质↑） |
| `CHLORINE` | **漂白**：降低颜色/品质 |
| `ORGANIC_WASTE` / `MICROBE` | **包裹体/生物缺陷**：品质↓；少量可做"苔藓包裹"特色 |
| `PLANT_MATTER` | 绿色系包裹体（祖母绿的"花园"效果） |
| `DEBRIS` / `PHYSICAL_CONTAMINANT` | **物理裂纹源**：裂晶风险↑ |
| `DREAM_CAP_EXTRACT` / `NIGHTMARE_EXTRACT` | **梦晶/幻晶**稀有变种 |
| `ZYANIDE` | **毒晶**：稀有但违禁 |
| `FANTA` / `SUGAR` / `ALCOHOL` | 趣味变种（汽水晶/酒晶） |
| `WINE_EXTRACT` | "酒红晶"彩蛋 |

于是玩法变成：

```
选水源（沟渠/锈水/幽水/纯净） → 读成分 → 加矿盐/致色剂/稳定剂调配方
   → 培育槽 → 按成分消耗与反应生长 → 成色/品质/变异
```

- **便宜水有特定成分**：锈水自带重金属 → 适合做红宝石；幽水带特殊有机物 → 适合做蛋白石；
  沟渠水杂质多 → 只能速生铺货。这比"纯度越低越差"有意思得多。
- **玩家会主动配液**：比如"锈水 + 还原剂 + 稳定剂"冲高饱和红宝石；
  "纯净水 + 矿盐 + 植物碎屑"做带包裹体的祖母绿。
- **废水也有成分**：矿物被消耗、污染物浓缩，倒掉或净化后循环。

### 11.6 注意

- 游戏存的是**成分类别**（重金属/矿物/酸…），不是"具体是哪种金属"。
  想要精确致色，可以给自己的矿盐/致色剂物品额外打标签（如 `GEM_DOPANT_CHROMIUM`），
  读取时"基础成分 + 自定义掺杂标签"一起算。
- 生长期间消耗成分：直接改容器的 `CURRENT_PART_*` 数值（`SetInt` / `RemoveContaminantFromContainer`），
  并刷新 `WaterFeatureHelper.UpdateWaterFeature(container, expose)` 让 tooltip 同步。
- 数值单位是"份"（1000/ml），做平衡时按 ml 换算，避免数值溢出。

---

## 12. 机器与模组：两台机器 + 一套模组

### 12.1 结论：不需要四台机器

你说得对。原版熔炉已经把答案摆出来了：

* 熔炉本体 `identifier = "furnace"`，`itemTypes = ["MACHINE"]`，自带模块仓；
* 模组用 `MODULE_TYPE_FURNACE` 标签声明兼容；
* 通用模组带 `BONUS_PERCENTAGE_PERFORMANCE_INT` / `EFFICIENCY` / `QUALITY` 加成；
* 特殊模组直接**改变用途**：`furnace_module_blast` 的效果是
  `BONUS_PERCENTAGE_EFFICIENCY_INT = -100` + `ADDITIONAL_EFFECT_STRING = "投入消耗增加\n-每晚产量增加\n-纯度降低"`，
  并且带 `BLAST_FURNACE_INPUT_COUNT` / `BLAST_FURNACE_OUTPUT_COUNT` 这类行为标签；
  另一个 `furnace_module_junk` 同类。

所以宝石培育只需要 **一台基础机 + 一台进阶机**，其余全部用模组表达：

| 机器 | 定位 | 模块仓 | 电力 | 默认环境 | 品质上限 |
|---|---|---|---|---|---|
| **基础结晶槽** `gem_growth_basic` | 放养/入门 | 2 格 | 无（或极低） | 常温常压、自然蒸发 | `fine` |
| **高压培育舱** `gem_growth_advanced` | 量产/冲高 | 4 格 | 有 | 可加压/控温/催化 | `perfect` |

两台机器共享同一套模组（`MODULE_TYPE_GEM`），进阶机只是**槽位更多、能装高阶模组、默认多一个压力腔**。
这样只多一件机器、一套图标，却保留了完整的成长线。

### 12.2 基础结晶槽（放养档）

- 晶核 + 溶液，摆在店里，**不需要电**；
- 每天溶液蒸发一点，水位过低停止生长甚至崩晶 → 补水；
- 默认只能出瑕疵/普通/优良；
- 放一个晶核 → 一颗大晶；放多个 → 一盘小晶；
- 灰尘/碎屑 → 随机包裹体。
- 2 个模块仓：装"过滤循环""速生""退火"这类基础模组，把它变成不同用途。

### 12.3 高压培育舱（进阶档）

- 接电；默认带基础温控，装压力舱后才能上高压；
- 高品质红/蓝/祖母绿必须在这里长（贴合现实的水热/助熔剂法）；
- 压力过高 → 爆釜；装稳压器降低概率；
- 4 个模块仓：温控 / 压力舱 / 稳压器 / 催化槽 可自由组合。

### 12.4 模组清单

**通用加成模组**（直接吃原版 `BONUS_PERCENTAGE_*`）

| 模组 | 效果 |
|---|---|
| 品质模组 `gem_module_quality` | `BONUS_PERCENTAGE_QUALITY_INT +10~30` |
| 效率模组 `gem_module_efficiency` | 减少耗水/耗电 |
| 性能模组 `gem_module_performance` | 提升生长速度 |

**环境模组**

| 模组 | 效果 | 可装 |
|---|---|---|
| 温控模组 | +温度档 | 两机 |
| 压力舱 | +压力档 | 进阶 |
| 稳压器 | -爆釜率 | 进阶 |
| 催化槽 | +催化档，提供催化剂槽 | 进阶 |
| 过滤循环 | 回收废水、减少换水 | 两机 |

**特殊用途模组（照抄 blast/junk 的思路）**

| 模组 | 改变用途 |
|---|---|
| 速生模组 | 生长速度↑、品质↓、水耗↑（类似 blast） |
| 精养模组 | 生长速度↓、品质↑、更耗电 |
| 退火模组 | 机器切换为**退火/稳定化**模式（陈化线） |
| 致色模组 | 固定偏向某种宝石/颜色（如"致色：红"） |
| 包体诱导模组（可选） | 故意制造包裹体，走"特色宝石"路线 |

> "速生"和"退火"正好对应熔炉两个特殊模组的"改用途"定位——一个走产量，
> 一个把机器变成陈化线；有了退火模组，就不必再为退火买第三台机器。

### 12.5 模组的数据范式（照抄熔炉）

机器：
```
identifier = gem_growth_basic / gem_growth_advanced
itemTypes  = ["MACHINE"]
EnableTag("MODULE_TYPE_GEM")
```

模组：
```
itemTypes = ["MODULE"]
EnableTag("MODULE")
EnableTag("MODULE_TAG")
EnableTag("MODULE_TYPE_GEM")
EnableTag("MODULE_TYPE", value="MODULE_TYPE_GEM")      // ITEM_TYPE_STRING 也写 MODULE_TYPE_GEM
EnableTag("BONUS_PERCENTAGE_QUALITY_INT")               // TagState.SetInt(...)
EnableTag("BONUS_PERCENTAGE_EFFICIENCY_INT")
EnableTag("BONUS_PERCENTAGE_PERFORMANCE_INT")
EnableTag("MODULE_TIER_INT")                            // 1~3
EnableTag("ADDITIONAL_EFFECT_STRING")                   // tooltip 里显示的效果说明
```

特殊模组再加自己的行为标签（如 `GEM_MODULE_FAST_GROWTH`、`GEM_MODULE_ANNEAL`），
由我们的补丁读取，等价于原版的 `BLAST_FURNACE_INPUT_COUNT`。

> 兼容判定具体是读 `MODULE_TYPE_GEM` 还是 `ITEM_TYPE_STRING`，需要实测；
> 原版熔炉两者都写了同一个值，我们也照写，双保险。

### 12.6 参数与公式

环境等级 `T / P / C`（0–3）由机器默认值 + 已装模组求和：

```
品质上限 = baseCap + T/2 + P/2 + C/2 + qualityBonus    // 收束到 0..4
生长速度 = baseSpeed * (1 + 0.15*T) * (1 + 0.1*P) * (1 + 0.1*C) * performanceBonus
裂晶率   = baseCrack + max(0, T - 安全温度)*k1 + max(0, P - 釜体上限)*k2 - 稳压/稳定修正
爆釜率   = max(0, P - 釜体上限) * k3                    // 只有装了压力舱才可能
```

`qualityBonus` / `performanceBonus` 可以直接来自
`MachineryHelper.GetCurrentQualityBonus(machine)` / `GetCurrentPerformanceBonus(machine)`，
前提是它们确实汇总 `BONUS_PERCENTAGE_*` 标签（签名支持，行为需实测）。

### 12.7 用原版机制实现

- **机器本体**：克隆 `MachineFurnace.Furnace()`（自带模块仓/电池槽/窗口），
  改名字、图标、`identifier`，再把兼容标签换成 `MODULE_TYPE_GEM`；
  或 `ItemDirectory.CreateEmptyItem` + `MachineryHelper.InitMachinery(g, 功率, true)` + 自建模块仓。
- **模块仓**：`MachineHelper.GetModuleInv(machine)`；槽位扩展参考 `ModuleBayExpansionKit`。
- **供电**：`MachineHelper.CanPower` / `TryDrawCyclePower`。
- **进度**：`GEM_GROW_DAY`/`NEED` 存机器 tag，`PlayerStore.EndNight` 推进；
  或原版 `MachineProgressHelper.InitProgressTypeMachine / StartProgressTypeMachine` 驱动进度条。
- **窗口**：沿用 ItemForge ProcessingContainer 窗口或原版机器窗口。
- **tooltip**：`ADDITIONAL_EFFECT_STRING` + `MachineryHelper.AddTooltipModuleBonus`。

### 12.8 为什么这样更好

1. **物品更少**：2 台机器 + 10 个模组，比 4 台机器 + 模块更好维护、更少图标。
2. **更符合原版直觉**：玩家已经会"给机器插模组"，不需要学新概念。
3. **深度在模组里**：速生/精养/退火/致色/包体诱导都是模组，组合空间大。
4. **成长线清晰**：基础槽 → 攒钱买进阶舱 → 收集模组，钱有地方花。
5. **不同宝石走不同工艺**：蛋白石基础槽就能养；高品质红/蓝/祖母绿要进阶舱 + 压力舱。

### 12.9 结论

- **放养就是基础机的默认玩法**，不需要额外机器；
- **高压/催化是进阶机的模组**，不是第四台机器；
- 特殊模组（速生、退火）负责"改用途"，对应熔炉的 blast/junk；
- 最终只做 **2 台机器 + 一套模组**。
