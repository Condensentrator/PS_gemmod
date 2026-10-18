# NPC 鉴定与真伪系统研究（Probably Stolen 0.46D）

## 1. 结论先行

原版的“品质 / 真伪 / 鉴定”不是单独的一套系统，而是同一个 `ItemFeature` + `ItemCondition` 机制：

- 每个物品可以挂若干 `ItemFeature`（状况、纯度、真伪、新鲜度……）。
- 一个 `ItemFeature` 同时可以带 **真实状况**（`realCondition`）和 **对外宣称/标签状况**（`fakeCondition`）。
- 未暴露时，提示框和 NPC 报价用的是 fake 那一份；被工具或顾客识破后，`ExposeFeature()` 切换到 real，播放揭穿对话，并触发价格修正。
- 因此“NPC 识别真伪”= 顾客在交易/议价时检查物品特征能否被揭穿（`CanExposeFeature`），而不是另外写了一段鉴定 AI。

## 2. 关键类型与字段

### ItemCondition

`Il2Cpp.ItemCondition` 是一条具体状况/品质：

| 字段 | 作用 |
|---|---|
| `display` | 显示名，例如“药用级” |
| `identifier` | 条件 id，例如 `pharma_grade` |
| `modValue` | 价值修正（百分比，正负都有） |
| `modValueDisplay` | 显示用的修正值 |
| `category` | 分类 id，例如 `item_feature_category_chemical_purity` |
| `hiddenAsPublic` | 是否对顾客隐藏 |
| `customValue` / `compareValue` | 自定义数值 / 比较值 |
| `onActivateActionId` | 激活时执行的动作 |

### ItemFeature

`Il2Cpp.ItemFeature` 是挂在物品上的一个“特征实例”：

| 字段 | 作用 |
|---|---|
| `featureType` | Normale / Event / Special / Temporary / TemporaryBuying / TemporarySelling / ClientTemporary |
| `valueStage` | Innate / Market / Final |
| `realCondition` | 真实状况 |
| `fakeCondition` | 假标签/宣称状况 |
| `useCondition` | 当前正在使用哪一份 |
| `usePreExposeValue` / `preExposeValueModifier` | 暴露前使用的价值修正 |
| `isExposable` | 能否被揭穿 |
| `isFeatureExposed` / `isExposing` | 是否已暴露 / 正在暴露 |
| `isFeatureDiscovered` | 玩家是否已发现 |
| `isPublicHidden` | 是否对公众隐藏 |
| `publicDisplay` / `actualDisplay` | 对外显示 / 真实显示 |
| `valueModifier` | 当前价值修正 |
| `parentItemUniqueId` | 所属物品 |

相关方法（来自 interop 签名）：

- `CanExposeFeature()`
- `ExposeFeature(bool lowkey = false, bool playBad = false)`
- `SetConditions(ItemCondition real, ItemCondition fake)`
- `GetCurrentItemCondition()`
- `GetCurrentValueMod()`
- `GetToolNeeded()` —— 揭穿需要的工具 id
- `GetExposeDialog()` / `CreateCustomerConfrontDialog()`
- `GetFormattedFakeModifier()` / `GetFormattedActualModifier()` / `GetExposedString()`
- `IsRealFeatureCheaper()` —— 真实状况是否比假标签更便宜
- `DiscoverFeature()` / `IsFeatureDiscovered()`

### GameItem 侧

- `itemFeatures`：物品上的特征列表。
- `AddItemFeature(feature)` / `RemoveItemFeatureByID(id)`。
- `FindItemFeatureByID(id)` / `FindItemFeatureByCategory(category)`。
- `IsConditionActive(id)`。
- `AccumulateFeatureStages(ref innateMult, ref marketMult, ref finalFactor, skipEventFeatures)` —— 汇总各阶段价值修正。

### 条件/特征工厂

- `ItemConditionList`：静态条件列表和工厂方法（`CreateLabPure`、`CreatePharmaGrade`、`CreateReplica`、`CreateGenuineCigarette` 等），以及 `conditionLookup`。
- `ItemFeatureList`：`CategoryNames`（分类 id → 显示名）、`SpecialFeature`、各种 `...Feature()` 工厂。

## 3. 原版“梦尘”的化学纯度分级

游戏内的书 `book_chem_p5_chem_purity_rating` 写得很明确：

> 部分顾客会携带化学扫描仪，核验产品标签上标示的纯度。

| 等级 | 条件 id | 纯度 | 价格修正 | 本地化文本 |
|---|---|---|---|---|
| 纯品级 | `lab_pure` | 99% 以上 | 基础价值 +200% | 实验室纯级 |
| 药用级 | `pharma_grade` | 90%–99% | 基础价值 +125% | 药用级 |
| 近药用级 | `near_pharma` | 80%–90% | 基础价值 +75% | 近药用级 |
| 街头标准级 | `street_standard` | 60%–80% | 无价格修正 | 街头标准 |
| 掺兑级 | `cut_product` | 45%–60% | 基础价值 -45% | 掺兑级 |
| 垃圾级 | `junk_grade` | 30%–45% | 基础价值 -75% | 垃圾级 |
| 淀粉级 | `starch_grade` | 低于 30% | 基础价值 -95% | 淀粉级 |

对应方法：

- `ChemicalProductHelper.GradeProduct(GameItem)` —— 按纯度给物品打分/挂条件。
- `GetGradeNumber(string conditionId)` / `GetGradeDifference(a, b)` —— 等级数字和等级差。
- `SetPurityFeature(GameItem, ItemCondition, ItemCondition)` / `SetPurityFeature(GameItem, ItemCondition)`。
- `GetPurity(GameItem)` / `HasChemicalPurity(GameItem)`。
- `WillAcceptNarcotic(GameItem, int)`。

## 4. NPC 揭穿流程（从字段/对话反推）

1. 物品生成时写入两份条件：
   - real：真实纯度/真伪；
   - fake：标签上宣称的纯度/真伪。
2. `ItemFeature` 默认使用 fake，tooltip 显示假标签，NPC 初始报价也按 fake 的 `modValue` 算。
3. 顾客带对应扫描工具（梦尘是“成分光谱仪” `chem_scanner`）时，在交易/议价阶段调用 `CanExposeFeature()`。
4. 揭穿成功 → `ExposeFeature()`：
   - 切换到 real 条件；
   - 播放揭穿对话；
   - 用 real 的 `modValue` 重算价格。
5. `IsRealFeatureCheaper()` 决定顾客是降价、拒绝还是惊讶加价；`GetGradeDifference` 用于算等级差。

对应的中文对话键（`Dialogue` 表）：

| 键 | 文本 |
|---|---|
| `dialog_expose_chemical_pharma_grade` | 这是药用级，不是实验室纯级。 |
| `dialog_expose_chemical_near_pharma` | 这只是近药用级。 |
| `dialog_expose_chemical_street_standard` | 这只是街头标准级而已。 |
| `dialog_expose_chemical_cut_product` | 这掺假了！ |
| `dialog_expose_chemical_junk_grade` | 纯度这么低，连瘾君子都不会要。 |
| `dialog_expose_chemical_starch_grade` | 这分明只是营养果淀粉。 |
| `chem_cut_player` | 纯度标错了。 |
| `chem_cut_client` | 好吧，好吧，我会降价。 |
| `chem_client_didnt_know` | 我之前不知道。我会降价。 |
| `chem_incorrect_client` | 好吧，我会调整价格。 |
| `chem_purity_higher_client` | 哇，多谢你告诉我。 |
| `dialog_expose_water_scam_1` | 这水的纯度和标签上标的不一样！ |

水质走的是同一套：

- `WaterFeatureHelper.GetConditionFromPurity(int purity)`；
- `WaterFeatureHelper.UpdateWaterFeatureFake(GameItem, int fakePurity)`；
- `WaterFeatureHelper.GetWaterFeatureDialog(ItemFeature)`；
- `dialog_expose_water_*` 系列对话。

香烟 / 食品券 / 免疫宁则直接使用 genuine/counterfeit 条件：

- `ItemConditionList.conditionGenuineCigarette` / `conditionGenuineStamp` / `conditionGenuineInjector`；
- `ItemConditionList.conditionReplica` / `conditionFunctional`（奢侈品仿制品/功能正常）；
- `ItemFeatureList.CigaretteAuthenticity`、`StampAuthenticity`、`InjectorFeature(actual, fake)`。

## 5. 对宝石 mod 的启示

### 现在已实现的方案

- 品质同时存在两套数据：
  - 真实品质：`GEM_QUALITY`、`GEM_QUALITY_SCORE`、`GEM_IMP_SUM`、`GEM_IMP_COUNT`、`GEM_INCLUSION`；
  - 标签品质：`ItemFeature`（分类 `item_feature_category_gem_quality`）的 `fakeCondition`。
- 6 个 `ItemCondition` 注册进 `ItemConditionList.conditionLookup`：
  劣质 / 普通 / 优良 / 极优 / 无瑕 / 包裹体，`modValue` 分别 -75 / -45 / 0 / +75 / +125 / +200。
- 标签打印机可直接编辑宝石标签：
  - `IsEditableLabelFeature` 接受我们的 feature（`FeatureType.Normale` + 分类已注册）；
  - 下拉选项来自 `conditionLookup[item_feature_category_gem_quality]`；
  - 改标签后 `PatchGemLabel` 刷新宝石名字/描述。
- 价格不再乘在 `unitValue` 上，而是由原版 `ItemFeature` 的 `modValue` 计算；
  `unitValue` 只保留重量基础价。
- 生长只更新 `realCondition`；玩家手动贴过的标签用 `GEM_LABEL_CUSTOM` 锁定，不会被自动同步掉。
- 卖出时如果标签价值 > 真实价值：
  - `SoldItem.TrackItem` 记录；
  - `PlayerStore.EndNight` 调用 `SecData.CommitCrime("fraud", soldPrice)`；
  - 同时尝试调用 `ChemicalFeedbackHelper.QueueChemicalReturningClient`，排队次日退款。

### 还没做的部分

- 顾客主动用扫描仪“当场识破”宝石标签：目前是卖出后结算欺诈，不走 `ExposeFeature` 的即时对质。
- 自定义“宝石扫描仪”工具和对应对话资源，尚未实测。
- 退款流程复用了化学品的返回顾客队列；如果原版对物品类型有额外假设，可能只出证据、不出现退款对话。

## 6. 假标签的两条后果：证据条与退款

这两件事是分开的，但都由 `SoldItem`（售出记录）串起来：

- **证据条 / 调查进度**：`SecData` + `PlayerStore.CheckCrime` → `CommitCrime` → `evidenceLevel` → 治安部上门。
- **退款 / 赔偿**：`SoldItem.HandleSoldItem` → `ChemicalFeedbackHelper` → 次日顾客回来 → 接受退款（扣钱）或拒绝（掉声望）。

### 6.1 售出记录

`SoldItem.TrackItem(GameItem, StoreClient, soldPrice)` 在 `PlayerStore.EndNight` 里记录：

| 字段 | 作用 |
|---|---|
| `itemId` / `itemName` / `itemUniqueId` | 定位卖出的物品 |
| `clientId` / `clientName` / `factionId` | 顾客与派系 |
| `soldPrice` | 成交价，后面算证据和退款都用它 |
| `isPersonalUse` / `isClientAddict` | 用途/成瘾，影响反馈 |
| `CanReturn` / `CanReturnAsBuyer` | 是否允许退货 |
| `alwaysFeedback` / `feedbackLevel` | 是否必定反馈、反馈强度 |

`SoldItem.HandleSoldItem()` 也在 `PlayerStore.EndNight` 里执行，处理上一批售出记录。

### 6.2 证据条：`SecData`

`SecData` 的字段就是证据/犯罪记录：

| 字段 | 作用 |
|---|---|
| `crimeTypes` / `crimeAmount` / `crimeAmountTotal` | 罪名、当前犯罪值、累计犯罪值 |
| `evidenceLevel` | 调查进度（0–100），UI 键 `inte_sec_evidence_level` = 调查进度 |
| `badChemTrackerFraud` | 化学假标签的欺诈待结算金额 |
| `badChemTrackerEndangerment` | 危险化学品的危害待结算金额 |
| `dirtyWaterTracker` / `mislabeledWaterTracker` | 脏水/贴错标签水的待结算量 |
| `bribeTracker` / `badAmmoTracker` / `badAmmoTrackerLegal` / `endangermentItemTracker` | 其他罪名 |

**化学假标签的记账规则**（`SecData.CheckSoldChem`，反汇编确认）：

```
diff = GetItemFeedbackLevel(item)
if (IsNarcotic(item)) diff -= GetEuphoriaLevel(item)

if (diff <= 0) return
if (diff <= 3) badChemTrackerFraud += soldPrice
else            badChemTrackerEndangerment += soldPrice * 2
```

`SoldItem.HandleSoldItem` 会调用 `CheckSoldChem`，所以化学品的假标签是在**当晚结算**时记账，不是在成交瞬间。

**待结算金额转成罪名**（`PlayerStore.CheckCrime`，也在 `EndNight`）：

- 每项 tracker 每天先自然衰减 5 点（脏水/贴错水/贿赂）。
- `badChemTrackerFraud >= 300`：每次提交 300 的 `fraud`（欺诈性虚假陈述），剩余继续累积。
- `badChemTrackerEndangerment >= 300`：每次提交 300 的 `endangerment`（危害消费者安全）。
- 脏水/贴错水：`>= 2500` 时提交 `min(tracker, 500) * 20`，即单次最多 10,000 的犯罪值。
- 贿赂/弹药/危害物品也各有阈值，逻辑相同：达到阈值后按固定块提交，余量保留。

`CommitCrime(crimeID, amount)` 会先经过 `ApplyCrimeModifier(client, value)` 修正（派系、顾客、perk，例如 `rep_perk_LLTIGHTLIPS` 让下层区顾客留下的证据更少），然后分别加到 `crimeAmount` 和 `crimeAmountTotal`。

**调查进度增长**（`SecData.OnNewDay`，`PlayerStore.BeginDay` 调用）：

- `crimeAmount` 每天衰减 20（当 `evidenceLevel < 80`），所以证据会“冷却”。
- 每天按当前犯罪总值增加 `evidenceLevel`：

| 当前犯罪总值 | 每日调查进度 |
|---|---|
| < 100 | +0 |
| 100–299 | +2 |
| 300–849 | +6 |
| 850–1199 | +9 |
| 1200–1499 | +12 |
| 1500–2499 | +20 |
| ≥ 2500 | +33 |

- 之后还会乘上调查员/perk 修正（例如减半、×1.2、×1.33、×1.5），最后封顶 100。
- 到 100 后走 `SendOfficer()` → `StoreClientListSecurity.CreateSecurityOfficer()`，由治安官上门：
  - `dialog_sec_fine_intro`：我们掌握了你多项犯罪行为的证据；
  - `GetFineValue()` 实际就是 `GetTotalCrimeValue()`；
  - 拒缴 → `dialog_sec_arrest_intro` 逮捕；
  - `GetProjectedSentence()` 决定罚款/监禁/处决。

### 6.3 退款 / 赔偿

次日反馈流程（`SoldItem.HandleSoldItem` / `ChemicalFeedbackHelper`）：

1. `SelectEligibleChemicalItem()` 从售出记录里挑出可反馈的化学品。
2. `GetPurityGradeDifference(item)` 比较真实等级和标签等级。
3. `GenerateReaction(item, soldItem, relatedItems)` 生成顾客反应台词。
4. `QueueFeedbackClient(soldItem, relatedItems, lines, isAskingCompensation, reputationLoss)` 或 `QueueChemicalReturningClient(soldItem)` 把顾客排进次日队列。
5. 次日对话：
   - `dialog_chem_feedback_returning`：你好，我昨天来过。
   - `dialog_chem_feedback_refund_only`：我要退款！
   - `dialog_chem_feedback_refund_compensation`：我要退款，还要赔偿！
   - 接受：`dialog_chem_feedback_choice_accept` = [接受（-{0}信用点）]；
   - 拒绝：`dialog_chem_feedback_choice_refuse` = [拒绝（{0}点声望）]。
6. `ChemicalFeedbackHelper.CanReport(item, soldItem)` 决定顾客是否 additionally 向治安部举报；举报会走 `SecData.CommitCrime`，和证据条联动。

注意：退货是**顾客事件**，不是自动扣款；玩家可以选择拒绝，代价是声望。

## 7. 宝石 mod 的实际接入

原版只对“化学品”调用 `CheckSoldChem`，宝石不会自动进入这条链路，所以 mod 自己补了：

1. `GemQuality.cs`
   - 注册 `item_feature_category_gem_quality` 和 6 个 `ItemCondition`；
   - 给宝石创建/更新 `ItemFeature`：`realCondition` = 真实品质，`fakeCondition` = 标签品质；
   - 生长只改真实品质，手动标签用 `GEM_LABEL_CUSTOM` 锁定。
2. `PatchGemLabel.cs`
   - postfix `LabelerElement.OnDropdownValueChanged`，标签改完后刷新宝石名字/描述。
3. `PatchGemFraud.cs`
   - postfix `SoldItem.TrackItem`：记录标签价值 > 真实价值的卖出；
   - postfix `PlayerStore.EndNight`：`SecData.CommitCrime("fraud", soldPrice)`，并尝试复用原版 `SoldItem` 调 `ChemicalFeedbackHelper.QueueChemicalReturningClient` 排队退款。
4. 价格不写进 `unitValue`，由原版 `GameItem.GetCurrentValue` 汇总 `ItemFeature` 的 `modValue`；这样改标签会直接改售价，和梦尘一致。

仍然没做：
- 顾客拿扫描仪当场 `ExposeFeature` 对质（目前是售后结算）；
- 自定义宝石扫描仪工具；
- 如果原版化学品退货队列对物品类型有额外假设，可能只出证据、不出现退款对话。

## 8. 相关类型索引

- `Il2Cpp.ItemCondition`
- `Il2Cpp.ItemConditionList`
- `Il2Cpp.ItemFeature`
- `Il2Cpp.ItemFeatureList`
- `Il2Cpp.GameItem`（`itemFeatures` / `AddItemFeature` / `IsConditionActive`）
- `Il2Cpp.ChemicalProductHelper`
- `Il2Cpp.WaterFeatureHelper`
- `Il2Cpp.AppraisalHelper` / `Il2Cpp.AppraisalServiceHelper` / `Il2Cpp.StoreClientListAppraisal`
- `Il2Cpp.ChemScannerUIManager` / `Il2Cpp.WaterScannerUIManager`
