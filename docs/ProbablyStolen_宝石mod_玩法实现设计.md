# 「宝石工房」玩法实现设计

> 配套文档：`ProbablyStolen_宝石mod_计划书.md`
> 本文只谈**玩法怎么落地**：状态机、数据、交互、概率、补丁点。
> 已核实的 API 用 `代码字体` 标出；未验证的内部流程会明确标注「需实测」。

---

## 0. 先定三个实现原则

1. **真相在"鉴定那一刻"才掷骰**。原石不预存隐藏属性 → 不需要在建物品时分配状态，也不会有"两颗原石堆叠后真相打架"的问题。
2. **种类×品质用具体 item id 承载**（`gem_ruby_fine`），而不是全靠标签。这样价格、堆叠、出售、图标全都天然正确，MVP 不用改定价函数。
3. **先跑通"买入→鉴定→切磨→培育→卖出"闭环**，再加误判/识破/镶嵌这些"风味层"。

---

## 1. 玩法状态机

```
┌──────────────┐   鉴定(付费/掷骰)   ┌──────────────────┐   切磨(工具/台)   ┌──────────────────┐
│ 原石          │ ─────────────────▶ │ 裸石              │ ───────────────▶ │ 精品裸石          │
│ gem_raw_*    │                    │ gem_<种>_<档>      │                  │ gem_<种>_<档+1>   │
└──────┬───────┘                    └────────┬─────────┘                  └────────┬─────────┘
       │ 盲卖(低价)                           │ 直接卖                              │ 镶嵌(M3)
       ▼                                     ▼                                     ▼
   给商人换小钱                            给商人/客户                          戒指/吊坠
                                            
┌──────────────┐   培育(机器+天数)   ┌──────────────────────────┐
│ 晶核(一次性)  │ ─────────────────▶ │ 培育宝石                  │
│ gem_core_*   │                    │ gem_<种>_<档>_cultured    │ 价值×0.4，可被识破
└──────────────┘                    └──────────────────────────┘
```

**核心张力**：原石是"彩票"，鉴定是"刮奖"，培育是"印钞机但钞票有记号"。

---

## 2. 数据模型

### 2.1 原石（4 个 id）

| id | 来源 | 期望价值 | 产出偏向 |
|---|---|---|---|
| `gem_raw_common` | 搜刮/便宜货 | 低 | 全种类，高瑕疵率，含假石 |
| `gem_raw_rare` | 稀有渠道/奢侈品客 | 高 | 全种类，中高品质概率↑ |
| `gem_raw_volcanic` | 火山/高温事件 | 中 | 偏红系（ruby/opal） |
| `gem_raw_deep` | 深矿/地下 | 中 | 偏蓝系（sapphire/emerald） |

原石本身**不入图鉴细节**：名字统一"XX原石"，描述"看不清内部"。真相在鉴定时掷出。

### 2.2 宝石（4 种 × 5 档 = 20 个 id）

- 种类：`ruby` / `sapphire` / `emerald` / `opal`
- 品质：`flawed`(瑕疵) / `plain`(普通) / `fine`(优良) / `rare`(稀有) / `perfect`(完美)
- id：`gem_ruby_flawed` … `gem_opal_perfect`
- 培育版：同 id 加 `_cultured` 后缀（再 20 个）
- **价值直接写在物品定义里**（`Value` 字段），例如：

| 品质 | 基础倍率 |
|---|---|
| flawed | 0.25× |
| plain | 0.5× |
| fine | 1.0× |
| rare | 2.2× |
| perfect | 5.0× |

种类基础价：ruby 160 / sapphire 140 / emerald 120 / opal 220（示例，后续调）。
培育版 = 同档 × 0.4，另加 `CULTURED` 语义（M2 用标签）。

### 2.3 标签（只在实际需要的实例上用）

| 标签 | 用途 | 何时加 |
|---|---|---|
| `GEM_CULTURED` | 培育货标识 | 培育产出时 |
| `GEM_TRUE_QUALITY` | 误判玩法里存真实档（M2） | 鉴定时 |
| `GEM_PERCEIVED_QUALITY` | 玩家看到的档（M2） | 鉴定时 |
| `GEM_FAKE` | 玻璃/染色假石 | 鉴定掷出时 |
| `GEM_LOCKED` | 培育中/已镶嵌，禁止误卖（可选） | 视需要 |

> 标签会随物品 state 存档（BrewingExpansion 全程依赖 `EnableTag/IsTag`，已验证可行）。

---

## 3. 鉴定怎么实现

### 3.1 交互载体：先"工具点物品"，后"鉴定台"

游戏原生工具交互是 `GameItem.MayTarget / CanTarget / Target(GameItem)`，
ItemForge 的"手持工具加工"就是 patch 这三个点。所以最省的鉴定交互是：

> **手持「珠宝放大镜」→ 点原石 → 掷骰 → 原石变成具体宝石。**

不需要新 UI，不需要机器窗口。鉴定台（批量、可加手续费/动画）放到 M2。

### 3.2 鉴定的判定逻辑（伪代码）

```csharp
// Harmony Postfix on GameItem.Target, only when tool is our loupe & target is gem_raw_*
static void OnAppraise(GameItem tool, GameItem target) {
    if (!IsLoupe(tool) || !IsRawGem(target)) return;

    var raw = RawTierOf(target.identifier);          // common/rare/volcanic/deep
    var species = RollSpecies(raw);                  // 按表加权
    var quality = RollQuality(raw);                  // 按表加权
    bool fake   = RollFake(raw);                     // 廉价原石才有假石
    var gemId   = fake ? "gem_glass" : $"gem_{species}_{quality}";

    var gem = DirectoryMaster.Item(gemId, true);
    CopyWeight(target, gem);                         // GeneralHelper.GetWeight / InitWeight
    if (fake) gem.EnableTag("GEM_FAKE", false);

    // 消耗原石、把宝石放回原格
    var slot = target.parentInventory;               // 具体取槽方式需实测
    target.Destroy();
    slot?.TryAddItem(gem);                           // 或 SlotMarker 方案，见 §7
}
```

### 3.3 概率表（可配置；第一版先硬编码在 C# 或配置里）

**种类权重**（示例）

| 原石 | ruby | sapphire | emerald | opal |
|---|---|---|---|---|
| common | 30 | 30 | 30 | 10 |
| rare | 25 | 25 | 25 | 25 |
| volcanic | 55 | 10 | 10 | 25 |
| deep | 15 | 40 | 40 | 5 |

**品质权重**（基础，再按原石档乘修正）

| 品质 | common | rare |
|---|---|---|
| flawed | 40 | 20 |
| plain | 30 | 30 |
| fine | 18 | 28 |
| rare | 9 | 15 |
| perfect | 3 | 7 |

**假石率**：common 8%，其余 0；假石产出 `gem_glass`（价值 5，`ContrabandLevel=1`）。

### 3.4 费用怎么收

三个选项，按接入成本排序：
1. **消耗一张「鉴定券」物品**（推荐 MVP）：原石+鉴定券都消耗，不需要碰钱。
2. **扣现金**：`PlayerStore.Instance.playerCash -= fee`（读到 `playerCash`，写入方式需实测）。
3. **公会服务**：M2 再接 `AppraisalServiceHelper` 的估价博弈。

### 3.5 "未鉴定"的显示（M1）

用原生 `ItemFeature` 做"公开显示 vs 实际显示"，这是游戏自带的假信息机制：
- 原石上挂一个 feature：`SetPublicDisplay("原石（未鉴定）")`、`SetActualDisplay("可能含有宝石")`；
- 鉴定后 `DiscoverFeature()` / `ExposeFeature()`，或直接换成具体宝石物品。
- 参考 `WineAppraisalMaster`：`ItemFeature.CreateFeatureFromNegociationData` +
  `SetPublicDisplay/SetActualDisplay` + `gameItem.AddItemFeature`。

> 由于我们让原石在鉴定时**直接变成具体宝石 id**，MVP 其实不需要 feature；
> feature 留给 M2 的"误判/假信息"玩法。

---

## 4. 切磨怎么实现

### 4.1 MVP：ItemForge 工具（零 C#）

用 ItemForge 的 `ItemTransformEnabled` 做「切割刀」：
```jsonc
{
  "Id": "gem_cutter", "DirectoryKind": "Tool",
  "Types": ["SYNTHESIS","TOOL"],
  "ItemTransformEnabled": true,
  "ItemTransformRecipes": [
    { "InputItemId": "gem_ruby_flawed", "InputCount": 1,
      "Outputs": [{ "ItemId": "gem_ruby_plain", "Count": 1 }] }
    // ×20 档位 ×4 种类
  ]
}
```
- 优点：完全配置化，立刻能玩。
- 缺点：确定性、无失败、无损耗 → 缺少"赌"的味道。

### 4.2 M2：加损耗与工艺

自定义 patch（沿用 ItemForge 的 `ProcessingToolMayTarget/CanTarget/Target` 结构）：
- 成功 85%，失败不出货且原石降一档（或直接碎成 `gem_shards`）；
- 「抛光轮」把 `fine` 有机会推到 `rare`，消耗抛光粉；
- 放大镜/工艺天赋提高成功率。

---

## 5. 人工培育怎么实现

### 5.1 MVP：ItemForge 加工机器（零 C#）

`gem_incubator`（`DirectoryKind: "Container"`，`PROCESSING_MACHINE`）：
```jsonc
"ProcessingMachineRecipes": [
  { "Name":"培育红宝石", "InputItemId":"gem_core_ruby",
    "OutputItemId":"gem_ruby_plain_cultured", "DurationDays":3,
    "UseInputWeight":false, "WeightRetentionPercent":100 }
]
```
- 晶核分种类（`gem_core_ruby/sapphire/emerald/opal`），**一次性消耗**。
- 培养液作为**另一件输入**：ItemForge 的加工机器一次只匹配一个输入 id，
  所以 MVP 用"晶核分品级 + 配方里要求成品档位"来近似，或干脆不要培养液。
- 优点：M0 就能跑；缺点：无随机、无变异、不读水质。

### 5.2 M1/M2：自定义每日 tick（推荐最终形态）

仿 `BrewingExpansion` 的 `PatchSeedToFruit`（它 patch `PlayerStore.EndNight`）：
```csharp
[HarmonyPostfix, HarmonyPatch(typeof(PlayerStore), "EndNight")]
static void TickIncubators() {
    foreach (var machine in PlayerStore.Instance.FindAllItem(true)) {
        if (!IsOurIncubator(machine)) continue;
        if (!MachineHelper.CanPower(machine)) continue;    // 断电暂停
        foreach (var core in machine.FindAllChildItems()) {
            if (!IsOurCore(core)) continue;
            int day = GetTag(core, "GEM_GROW_DAY") + 1;
            int need = GetTag(core, "GEM_GROW_NEED");
            SetTag(core, "GEM_GROW_DAY", day);
            if (day < need) continue;

            var quality = RollCulturedQuality(machine, core);  // 见下
            var gem = DirectoryMaster.Item($"gem_{species}_{quality}_cultured", true);
            gem.EnableTag("GEM_CULTURED", false);
            if (RollMutation()) gem = CreateVariant(gem);
            PlaceInto(machine, gem);
            ConsumeSolution(machine);
            core.Destroy();  // 一次性晶核，长成宝石即消耗
        }
    }
}
```
- 存档：进度存在**晶核/机器的 tag** 上（`GEM_GROW_DAY`/`GEM_GROW_NEED`），读档自动恢复。
- 断电：`MachineHelper.CanPower` 直接复用。
- 放置成品：`slotMarker = machine.TryFindOneValidInventorySlot(gem); slot.AcceptUnchecked(); inventory.Validate()`（BrewingExpansion 已验证这套调用）。

### 5.3 品质公式（培育）

```
score = 晶核品级基础档 + 培养液成分加成 + 机器等级加成 + Random(-1..1)
quality = clamp(score, 0, 4)
```
- 晶核品级：普通/稀有，决定基础档；**一次性，不继承、不迭代**；
- 培养液成分：水质纯度 + 矿物/重金属/酸碱（见培育系统设计第 11 节）；
- 变异率：2%（普通）/ 5%（实验添加剂），变异产物价值 ×1.5–3，仍带 `GEM_CULTURED`。

### 5.4 识破（M2）

- 培育货全部带 `GEM_CULTURED` 标签 + 名字带"培育"前缀 → **默认透明，不搞欺诈**，价值低是唯一代价。
- 若想做"洗白"，再让鉴定师按概率识破并扣声望/判违禁；这属于可选风味，MVP 不做。

---

## 6. 销售与定价

### 6.1 定价（MVP 不用写代码）

具体 id 的物品在定义里就有 `Value`，游戏 `GetValue` 直接用它。想按重量浮动时，
创建时 `GeneralHelper.InitWeight(item, grams)` + `PriceHelper.GetWeightedPrice`（鉴定/切磨时算一次写进 `unitValue`）。

### 6.2 卖出渠道

| 阶段 | 做法 | 成本 |
|---|---|---|
| MVP | 把宝石加进现有奢侈品小贩/收购表（`PSProcurement` 或原版 client 的货物表） | 低 |
| M3 | 自定义宝石商人：patch `StoreClientListDict.CreateStoreClient` 返回自己的 `StoreClient` | 中 |
| M3 | 奢侈品客户偏好宝石/首饰：patch `StoreClient.IsClientBuyingThisItem` | 中 |

`PlayerStore.Instance.AddDirectSellingItemToTable(item, false, false, false, 0)` 可把物品摆上柜台（BrewingExpansion 验证过）。

---

## 7. 技术分层（谁负责什么）

| 模块 | ItemForge 能做 | 必须自定义 patch |
|---|---|---|
| 物品注册/图标 | ✅ | |
| 切割工具（确定性） | ✅ | |
| 培育（定时确定性） | ✅ | |
| 鉴定（随机产出） | ❌ 只有原版随机表（junk/material/medical…），不能自定义宝石表 | **是** |
| 未鉴定/真假显示 | ❌ | **是**（`ItemFeature`） |
| 培育品质/变异/耗材 | ❌ | **是**（`EndNight` tick） |
| 按品质定价（标签式） | ❌ | **是**（`GetValue` postfix） |
| 商人/客户偏好 | ❌ | **是** |
| 天赋/事件 | ❌ | **是** |

### 必须写的 Harmony 补丁清单（M1–M3）

1. `GameItem.Target(GameItem)`（+ `MayTarget/CanTarget`）——放大镜鉴定。
2. `ItemFeature.GetPublicDisplay/GetActualDisplay`（可选）——未鉴定文案。
3. `PlayerStore.EndNight`——培育每日推进。
4. `GameItem.GetValue(...)`（可选）——按标签/重量动态定价。
5. `StoreClientListDict.CreateStoreClient`（可选）——宝石商人。
6. `StoreClient.IsClientBuyingThisItem`（可选）——客户偏好。
7. `GameItemElement.GetTooltip`（可选）——宝石专属 tooltip。

---

## 8. 存档与边界情况

| 情况 | 处理 |
|---|---|
| 原石堆叠 | 真相在鉴定时掷，原石可安全堆叠；鉴定时按"消耗 1 个"处理 |
| 鉴定时背包满 | 先确认能放成品再消耗原石；放不下就提示、不扣费 |
| 培育中途存档 | 进度存 tag，读档后 `EndNight` 继续 |
| 培育中删 mod | 机器/晶核变成无效物品，游戏可能显示异常；README 提示"删前先取出" |
| 断电/停机 | `MachineHelper.CanPower` 为 false 时暂停，不消耗 |
| 重复产出 | 消耗输入与产出必须在同一 tick 内完成，失败回滚 |
| 误卖 | 培育中给晶核/机器加 `GEM_LOCKED`，出售前检查（可选） |

---

## 9. 里程碑与验收

### M0（ItemForge，1–2 天）
- [ ] 原石 + 20 天然宝石 + 20 培育宝石 + 晶核（一次性）+ 切割刀 + 培育舱
- [ ] 切割刀：原石→普通宝石（确定性）
- [ ] 培育舱：晶核 → 3 天 → 培育宝石（晶核消耗）
- [ ] 物品能注册、图标正常、能卖出
- **验收**：能在游戏里完成"买晶核→培育→卖"一圈（原石线可并行验证）。

### M1（自定义鉴定，2–3 天）
- [ ] 放大镜 + `Target` 补丁：原石→随机宝石（种类/品质/假石）
- [ ] 鉴定券/费用消耗
- [ ] 背包满、堆叠、回滚处理
- **验收**：同一颗原石能鉴定出不同结果；假石能出现。

### M2（培育深度 + 误判，3–5 天）
- [ ] `EndNight` tick：品质公式、培养液成分、变异、断电暂停
- [ ] 可选：鉴定误判（真实档 vs 显示档）
- [ ] 培育货识别/价值折扣
- **验收**：培育有稳定产线和稀有变异；经济不崩。

### M3（经济与风味，3–5 天）
- [ ] 宝石商人 / 客户偏好
- [ ] 首饰配方
- [ ] 起始天赋、事件
- **验收**：有专门的宝石买卖渠道和成长线。

---

## 10. 待实测的点（动手前先验证）

1. `GameItem.Target` 补丁里**如何拿到被点物品所在的槽并安全替换**（ItemForge 的 `ProcessingToolRuntime` 里有现成写法，直接抄）。
2. `ItemFeature` 的必填字段（`identifier`/`category`/`featureType`），以及 `DiscoverFeature` 是否立刻刷新 tooltip。
3. `playerCash` 的写入方式（若选扣现金）。
4. 培育机器断电时 `CanPower` 的行为、以及 `FindAllChildItems` 是否包含嵌套容器。
5. 现有小贩货物表能否直接加自定义宝石（`PSProcurement` 的 catalog 机制）。

---

## 11. 一句话总结

**M0 用 ItemForge 把"原石、宝石、切割、培育舱"全部配置出来跑通闭环；M1 用一个 `Target` 补丁 + 掷骰表实现"鉴定"；M2 用 `EndNight` tick 把培育做成有品质/变异/耗材的系统；M3 再补商人和首饰。** 真相在鉴定时掷、种类×品质用具体 id——这两条能让 80% 的玩法不用碰复杂状态管理。
