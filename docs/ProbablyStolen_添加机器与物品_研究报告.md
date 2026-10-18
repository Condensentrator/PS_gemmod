# 《Probably Stolen》Demo 添加「机器 & 物品」研究报告

> 游戏：Probably Stolen Demo v0.46D（Unity 2021.3.45f2，IL2CPP，x64）
> 加载器：MelonLoader v0.7.3 Open-Beta（net6）
> 游戏根目录：`D:\steam\steamapps\common\Probably Stolen Demo`
> 日期：2026-10-13

---

## 0. 一句话结论

这游戏本体是 **IL2CPP + MelonLoader**，但社区已经围绕它建好了一套**数据驱动的内容框架 `ItemForge`**
（就是 `Mods/ItemForgeRuntime.dll`，市场里那个"物品工坊"）。所以给游戏加机器和物品有两条路：

| 路线 | 怎么做 | 适合 | 工作量 |
|---|---|---|---|
| **A. 写 ItemForge 内容包**（推荐） | 编一个 `PSItemPack_*.dll`，里面塞一份 JSON + 若干 PNG | 加普通物品、食物、鱼、**加工机器**、合成台、手持工具、盲盒式掉落 | 小，几乎不用写 C# |
| **B. 写独立 MelonLoader mod** | 用 Harmony patch 游戏原生 `ItemDirectory` / 机器 API | 要游戏原生没有的整机行为、要上架到特定 NPC、要改存档/经济逻辑 | 大，纯 C# |

> **A 路线已经落地验证**：本报告附带一个可直接编译的模板 `ItemForgePackTemplate/`，
> 内含 2 个物品 + 1 台「慢炖罐」机器，已编译成功并放进 `Mods/PSItemPack_Demo.dll`（见第 6 节）。

---

## 1. 环境事实（已核实）

* 游戏是 **IL2CPP**：逻辑在 `GameAssembly.dll`，Unity 元数据在 `Probably Stolen_Data/global-metadata.dat`。
* MelonLoader 会把 IL2CPP 程序集"翻译"成托管程序集，供 mod 引用：

  `MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll`（游戏本体代码，**类型在 `Il2Cpp` 命名空间下**）

  ⚠️ 这份程序集是 Il2CppInterop 生成的**桩**：`public` 类型/字段/方法签名**准确**，但方法体被换成
  `IL2CPP.il2cpp_runtime_invoke(...)`，**看不到字符串常量**（所以原版的物品标签字面量、配方内容读不出来，
  只能靠运行期 dump 或 mod 源码反推）。
* 现有 mod（`Mods/`）：`ItemForgeRuntime`（框架）、`PSItemPack_Gofishing`（钓鱼内容包，63 件物品）、
  `BrewingExpansion`（酿造大扩展，纯 C# 加机器）、`PSProcurement`（采购商店）、`PSPDA`、`PSMarketRequests`、
  `WineAppraisalMaster`、`ProbablyStolenInspector`（游戏数据检查器）等。
* 编译环境：本机 `.NET SDK 8/9` + `net6.0` 运行时齐备，`dotnet build` 可直接出 net6 程序集。

---

## 2. 游戏原生的物品 / 机器模型

### 2.1 物品 = 注册到 `Directory<GameItem>` 的**工厂函数**

```csharp
// Il2Cpp 命名空间
public class Directory<T> : MonoBehaviour {                 // 注意：是 MonoBehaviour
    public Dictionary<string, Func<T>> factoryDictionary;
    public virtual bool Add(string identifier, Func<T> factoryFunc);  // ← 注册
    public virtual bool Set(string, Func<T>);
    public virtual bool Remove(string);
    public virtual T Create(string identifier);
    public virtual void InitDirectory();                    // 各目录重写它来自我填充
}

public class ItemDirectory : Directory<GameItem> {          // 物品目录基类
    public static GameItem CreateEmptyItem(string identifier = null);  // 造空白物品
}

public class DirectoryMaster {                              // 全局注册表/查询入口
    public static bool AddDirectory<T>(Directory<T> d);
    public static T    Create<T>(string identifier);
    public static GameItem Item(string identifier, bool isOwned = true);  // ← 全游戏按 id 查物品
    public static bool Has<T>(string)/Remove<T>(); public static void Reset();
}
```

`ItemDirectory` 的子目录（决定物品"归哪类"）：`FoodItemDirectory`、`MedsItemDirectory`、
`GunsItemDirectory`、`FurnitureItemDirectory`、`ContainerItemDirectory`、`MaterialDirectory`、
`ToolDirectory`、`MiscItemDirectory`、`AmenitiesItemDirectory`、`WineDirectory`、`KeyItemDirectory`… 等。

**关键点**：物品不是 ScriptableObject，而是**普通托管对象**（`GameItem : GameItemFunc : Il2CppSystem.Object`），
运行时用 `DirectoryMaster.Item(id, true)` 拿到。

### 2.2 `GameItem` 的重要字段（设置新物品时用到的）

| 字段 | 类型 | 说明 |
|---|---|---|
| `identifier` | string | **注册 id**，即查询键 |
| `name` / `SetName(string)` | string | 显示名（游戏直接存字符串，不做 key 查表） |
| `identifierName` | string | 英文/技术名 |
| `shortDescription` / `longDescription` / `flavorText` | string | 说明/风味文本 |
| `unitValue` | long | 单价 |
| `spritePath` / `spriteAtlasPath` | string | 图标 key / 图集 |
| `useSpriteFromMod` / `spriteChanged` | bool | 用 mod 自定义图标 |
| `shape` / `modifiedShape` | `GridShape` | 网格占格（`new GridShapeBuilder(w,h).SetDataFill(w,h,1).Build()`） |
| `itemTypes` / `SetGameItemType(string)` | List\<string\> | **类别标签**（下面 §2.3） |
| `state` / `EnableTag(string,bool)` / `IsTag(string)` | `TagSystem` | 任意自定义标签（机器身份常靠它） |
| `recipeManager` / `FindAllRecipes()` | `RecipeManager` | 配方表 |

### 2.3 物品类别是**字符串标签**，不是枚举

`SetGameItemType` / `itemTypes` 用的词表（从 `UserData/PSProcurement-catalog.txt` 提取，权威）：

```
FOOD  PROCESSED_FOOD  BREVAGE  ALCOHOL  SUBSTANCE  TREAT  SEED  FARMING_SUPPLIES
MATERIAL  BASIC_MATERIAL  CHEMICAL_SUPPLIES  TOOL  WEAPON  MELEE_WEAPON  AMMUNITION
MEDICAL  NARCOTIC  POISON  HOUSEHOLD_GOOD  LUXURY_ITEM  CONTRABAND  JUNK  FILTER
ACCESS_CARD  WATER  VARIANT_WATER  WATER_RATION  WATER_PURIFICATION_SUPPLY  SUPPLY_CRATE
ENERGY_CELL  ART  MEDICAL_TOOL  MIDLINE  MUNITION …
```

（`ItemForge` 另有框架级伪标签：`FISH`、`PROCESSING_CONTAINER`、`PROCESSING_MACHINE`、`SYNTHESIS`，
它们**不会**被写进 `itemTypes`，只是开关。）

### 2.4 机器 = 带机器标签/功能的 `GameItem`，行为靠**静态 Helper**

游戏里**没有 `Machine` 基类**。每个 `MachineXxx` 都是**静态 helper 类**，对 `GameItem` 操作，例如：

```csharp
MachineFurnace.Furnace() -> GameItem            // 熔炉工厂：克隆它就继承"机器外壳+电池槽+窗口"
MachineBottlePrinter.BottlePrinter() -> GameItem
MachineFridge.Fridge() / MachineRecharger.Recharger() / MachinePurifier.WaterPurifier()
MachineHydroponic.*  MachineWineRack.AgeWell(...)  MachineBlender.*  MachineChemFinisher.*
```

机器通用机制在 `MachineryHelper`（供电/进度/加成）、`MachineHelper`（电池槽/模块仓/
`CanPower`/`TryDrawCyclePower`/`GetModuleInv`）、`MachineProgressHelper`（源物品→成品，按天推进）。
配方模型是 `CraftingRecipe`（`requiredMaterials`/`requiredTools`/`craftFunc`/`itemIdentifier`/`amount`）
→ 由 `CraftingHelper.InitCraftingTable(item)` / `InitVendingTable(item)` 生成 `CraftingRecipeManager` /
`VendingRecipeManager`，挂在 `GameItem.recipeManager` 上。

---

## 3. 路线 A —— 写 ItemForge 内容包（推荐）

`ItemForge` 在启动时扫描自己所在目录（`Mods/`）下所有 **`PSItemPack_*.dll`**，从每个包里读
**嵌入资源 `ItemForge.items.json`**（`BundleConfig`，大小写不敏感），再把物品逐个注入到对应的
`XxxItemDirectory.InitDirectory()`（Harmony Postfix）。物品图标是**嵌入的 PNG**，
资源名固定为 `ItemForge.icons.<物品Id>.png`。

### 3.1 一个内容包 DLL 必须包含

1. 一个空的 `ItemForge.ContentPack.ContentPackMod : MelonMod`（让 MelonLoader 加载它）。
2. 程序集特性：
   ```csharp
   [assembly: MelonInfo(typeof(ContentPackMod), "包名", "1.0.0", "作者", null)]
   [assembly: MelonGame("Questing Goose Studio", "Probably Stolen")]
   [assembly: MelonAdditionalDependencies("ItemForgeRuntime")]   // 保证框架先初始化
   ```
3. 嵌入资源 `ItemForge.items.json`（用 csproj 的 `LogicalName` 固定名字）。
4. 每个物品一个 `ItemForge.icons.<Id>.png`。

> 这正是 `PSItemPack_Gofishing.dll` 的结构（其 3 个 C# 类型全是空类/常量类）。

### 3.2 `ItemForge.items.json`（`BundleConfig`）顶层字段

```jsonc
{
  "RequiredRuntimeApi": 20,     // 必须 ≤ 运行时 API 版本（现为 20），否则整包跳过
  "GeneratorVersion": "1.0.0",
  "BundleId": "my_pack",
  "BundleName": "我的内容包",
  "Author": "作者",
  "Items": [ /* ItemConfig[] */ ]
}
```

### 3.3 单个物品 `ItemConfig` 字段（全部，含默认值）

**身份/文字**：`Id`(="custom_item") · `ChineseName` · `EnglishName` · `Value`(=100) ·
`ShortDescription` · `FlavorText` · `DirectoryKind`(="Misc") · `Types`(string[]) · `AdditionalTags`(string[])

**渲染/声音**：`SpriteAtlas`(="Items/items_food"，当前版本 **不生效**) · `ShapeTemplate`(="red_beer"，**不生效**) ·
`SoundPreset`("Glass"/"Tiny"/"Vial"/"SoftPlastic") · `GridWidth`/`GridHeight`(1–8，决定占格与图标尺寸)

**存储容器**：`StorageContainerEnabled` · `StorageContainerWidth`/`Height`(1–20) · `StorageContainerAllowedTag`

**可食用**：`ConsumableKind`("None"/"Food"/"Drink") · `Calories` · `ThirstRecovery`

**鱼（按重量计价）**：`FishEnabled` · `FishMinWeight`/`FishMaxWeight`(g) · `FishValuePerKg` · `FishCaloriesPerKg`

**从原版物品继承行为**：`MedicalTemplateId` / `WeaponTemplateId`（设了就克隆那件原版物品当底子）

**手持工具/武器（合成）**：`ItemTransformEnabled` · `ItemTransformInputId` · `ItemTransformOutputId` ·
`ItemTransformRecipes[]`（元素：`InputItemId` / `InputCount` / `UseInputWeight` / `Outputs[]{ItemId,Count,WeightYieldPercent}`）；
武器版：`WeaponTransformEnabled` / `WeaponInputItemId` / `WeaponOutputItemId`

**加工容器（工作台，手动点"开始"）**：`ProcessingContainerEnabled` · `ProcessingContainerWidth`/`Height`(1–12) ·
`ProcessingContainerMaxWeightGram` · `ProcessingContainerOutputId` · `ProcessingContainerWeightRetentionPercent` ·
`ProcessingContainerQualityThreshold` · `ProcessingContainerQualityValueBonusPercent` ·
`ProcessingContainerIngredients[]`（`MatchKind`=`ItemId`/`Fish`/`FishId`/`FishIdModifier`/`GameTag`，`MatchValue`，
`MinCount`/`MaxCount`/`Required`/`QualityBonusPerUnit`/`ValueBonusPercentPerUnit`）·
`ProcessingContainerRecipes[]`（`Name`/`OutputId`/`Ingredients`/`ProcessMode`=`Instant`/`Timed`/`Duration`/`DurationUnit`/`ConsumeContainer`…）

**★ 加工机器（放料后按天自动产出）**：`ProcessingMachineEnabled` · `ProcessingMachineWidth`/`Height`(1–12) ·
`ProcessingMachineRecipes[]`，每条：
```jsonc
{ "Name":"慢炖果酱", "InputItemId":"demo_berry", "OutputItemId":"demo_jam",
  "DurationDays":2, "UseInputWeight":false, "WeightRetentionPercent":100.0 }
```
（机器本体 `DirectoryKind` 用 `"Container"`，`Types` 带 `"HOUSEHOLD_GOOD","PROCESSING_MACHINE"`，
`AddContainerTag:true`。输出可写 `"table:xxx"` 走随机表。）

**搜刮掉落**：`ScavengeBonusEnabled` / `ScavengeTargetItemId(s)` / `ScavengeTargets[]` / `ScavengeBonusChance` /
`ScavengeReplaceOriginal` / `ScavengeExclusiveMinCount`/`MaxCount`/`Priority`
**客户上门掉落**：`CustomerDropEnabled` / `SellerIds[]` / `SellerId` / `DropChance` / `CooldownMs`
**违禁品**：`ContrabandLevel`(0–4) · `StolenHeat`(0–100)　**兑换**：`ExchangeEnabled` / `ExchangeIntents[]`

### 3.4 注入流程（框架内部，已核实）

```
Core.OnEarlyInitializeMelon → ItemRuntime.LoadPacks()（扫 PSItemPack_*.dll，读 ItemForge.items.json）
Core.OnInitializeMelon      → ItemDirectoryRouter.Install() 给需要的目录挂 RegisterItemPatch.Postfix
                              （任一目录挂不上就整体回退，避免半注册）
RegisterItemPatch.Postfix(dir):
    ((Directory<GameItem>)dir).Add(cfg.Id, () => CreateItem(cfg));
```
`CreateItem` 会设好 id/名字/描述/`spritePath=Id`/`useSpriteFromMod`/`GridShape`/`unitValue`/类别标签…，
再按开关初始化容器/机器/工具/存储行为，最后处理违禁品等级。

### 3.5 图标

* 一张 PNG = 一个物品图标，资源名 **`ItemForge.icons.<Id>.png`**，建议尺寸 `GridWidth*16 × GridHeight*16`。
* 运行时把 PNG 变 `Sprite` 塞进游戏的 `ModLoader.Mods`，并 Hook `GameItemElement.ResolveSpriteByName` /
  `ResolveItemSprite` / `SpriteDict.GetSprite` 三个取图点做替换。**没有外部图片路径**，只能嵌入。

### 3.6 怎么让玩家**获得**

ItemForge 只负责"注册进游戏"，**不自动上架商店**。可用的获得方式：
* `ScavengeBonus*`（搜刮时按概率替换/额外产出）——最省事；
* `CustomerDrop*` + `SellerIds`（指定门客/小贩上门时掉进他们的货物）——真实钓鱼包就是这么给风干架的；
* 或走路线 B，自己 patch 商店/NPC（见 §4）。

---

## 4. 路线 B —— 写独立 MelonLoader mod

当 ItemForge 的模型不够用（要原生整机行为、要特定 NPC 上架、要动经济/存档）时，直接写 C# mod。
`BrewingExpansion.dll` 就是范本（70 个源文件，反编译可读）。

### 4.1 注册物品/机器

```csharp
// Harmony Postfix 挂在某个具体目录的 InitDirectory 上，跑在原版注册之后
[HarmonyPatch(typeof(FoodItemDirectory), "InitDirectory")]
static void Postfix(ItemDirectory __instance) {
    Func<GameItem> f = () => BuildMyMachine();
    ((Directory<GameItem>)(object)__instance)
        .Add("my_machine_id", Func<GameItem>.op_Implicit(f));
}

static GameItem BuildMyMachine() {
    GameItem g = MachineFurnace.Furnace();          // 克隆原版机器 → 自带机器外壳/电池槽/窗口
    g.SetName("我的机器"); g.SetValue(300);
    g.SetSprite("", "my_machine_id");
    g.shape = new GridShapeBuilder(3,3).SetDataFill(3,3,1).Build();
    // 或从零：ItemDirectory.CreateEmptyItem("id") + itemTypes={"MACHINE"} + MachineryHelper.InitMachinery(g,5,true)
    return g;
}
// 运行时实例化：DirectoryMaster.Item("my_machine_id", false)
```

### 4.2 行为与配方

* `BrewingExpansion` **完全不用** `CraftingRecipe/RecipeManager`，而是用 **Harmony patch + 自定义标签**驱动：
  例如"种子孵化器"= 克隆熔炉 + patch `PlayerStore.EndNight`，夜里检查机器里的种子 → 生成对应果实
  （`MachineHelper.CanPower` 供电判定 → `item.FindAllChildItems()` 取内容 → `DirectoryMaster.Item(果实,true)`
  → 找空槽 `TryFindOneValidInventorySlot` 放进去 → 销毁种子）。
* 机器身份用自定义标签识别：`item.EnableTag("BE_FORBIDDEN_YEAST_REACTOR", false)`，
  判断 `item.IsTag(...)`。

### 4.3 上架/获得

```csharp
PlayerStore.Instance.AddDirectSellingItemToTable(item, false, false, false, 0);
// 或 patch StoreClientListDict.CreateStoreClient 返回自定义 StoreClient（原版小贩）
PlayerStore.Instance.IsPlayerOwnThisItem("id")
```

### 4.4 需要的引用

编译路线 B 要引用 `MelonLoader/net6/*.dll`（MelonLoader、0Harmony、Il2CppInterop.Runtime）
**加上** `MelonLoader/Il2CppAssemblies/` 里用到的游戏/Unity 程序集。目标框架同样 `net6.0`、`x64`。

---

## 5. 关键坑 / 注意

1. **API 版本**：`RequiredRuntimeApi` 必须 ≤ `ItemForgeRuntime` 当前版本（现 20），否则整包被静默跳过。
2. **文件名前缀**：内容包必须叫 `PSItemPack_*.dll`，且和 `ItemForgeRuntime.dll` 同目录，否则发现不了。
3. **资源名精确**：JSON 必须是 `ItemForge.items.json`，图标必须是 `ItemForge.icons.<Id>.png`
   （用 csproj `LogicalName` 固定；否则会被 .NET 加上命名空间前缀而找不到）。
4. **目录一致性**：ItemForge 要求**所有**要用的目录都能挂上 patch，否则整体回退、物品全不注册。
5. **互不覆盖**：物品按 `Id` 去重，**先加载的包优先**，重复 id 会被忽略——起名加前缀避免撞车。
6. **IL2CPP 看不到方法体**：想照抄原版配方/标签字面量，得靠运行期 dump（`ProbablyStolenInspector`）
   或反编译现有 mod。
7. **本包是 demo**：`Mods/PSItemPack_Demo.dll` 只是演示，删掉即可还原，不影响存档（除非你已用其物品存档）。

---

## 6. 已交付物

* **研究报告**：本文件。
* **可编译模板**：`ItemForgePackTemplate/`（`dotnet build -c Release` 已通过，0 警告 0 错误）。
* **已部署的示例包**：`Mods/PSItemPack_Demo.dll`（7680 字节），内含：
  * 物品 `demo_berry` 示例浆果、`demo_jam` 示例果酱（食物）；
  * **机器** `demo_slow_cooker` 示例慢炖罐（加工机器：浆果 ×2 天 → 果酱）。
  * 3 张占位图标（`icons/*.png`）。
  卸载：删除 `Mods/PSItemPack_Demo.dll` 即可。
