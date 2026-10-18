# Probably Stolen · 宝石工房（GemBasic）

给《Probably Stolen》Demo 写的宝石培育 Mod。基于 **ItemForge** 内容包 + 一组 Harmony 补丁，
实现"晶核 + 培养液 → 按天生长的宝石"，并接入原版水质成分、重量、阶段、类型标签。

> 当前版本是**测试版**：数值、供货、高级机器都还没定稿，主要用于验证玩法闭环。

## 功能

- **三种矿物**
  - 刚玉：红 / 蓝 / 绿 / 黄 / 粉
  - 绿柱石：祖母绿 / 海蓝宝石 / 摩根石 / 金绿柱石
  - 石英：紫水晶
- **晶核**：一次性种子，用完即消耗；刚玉 / 红宝石 / 绿柱石 / 石英四种晶核用不同白模造型区分。
- **重量与阶段**：随机初始重量，之后每天增长（有上限），重量跨阈值自动换小/中/大/巨阶段和贴图。
- **水质成分**
  - 颜色只看无机成分比例：重金属 → 红/祖母绿，矿物 → 蓝/海蓝，化学 → 绿/摩根石，混合 → 黄/粉等；
  - 植物/有机废物只当**杂质**，不参与颜色，只降低生长速度；
  - 培养液必须是专用水槽里的液体容器，每次生长扣 100 ml。
- **机器**：自建窗口，一个专用水槽 + 一个 5×5 材料区（只接受带 `GEM_ITEM` 标签的物品）。
- **类型**：成品宝石显示 `[奢侈品] [宝石]`；`GEMSTONE` 类型通过 Unity Localization 注入 `Mechanic` 表的
  `type_gemstone` 显示为"宝石"。
- **贴图**：全部预染色（无运行时染色），每个颜色一套图标；晶核为白模。

## 依赖

- 游戏：Probably Stolen Demo v0.46D（IL2CPP，Unity 2021.3.45f2）
- MelonLoader 0.7.3（net6）
- **ItemForgeRuntime**（本仓库不包含；需单独安装，Runtime API 20）

## 构建

```bash
dotnet build -c Release
# 产物：bin/Release/net6.0/PSItemPack_GemBasic.dll
```

游戏不在默认目录时：

```bash
dotnet build -c Release -p:GameDir="E:\path\to\Probably Stolen Demo"
```

## 安装

1. 安装 MelonLoader + `ItemForgeRuntime.dll`；
2. 把 `PSItemPack_GemBasic.dll` 放进游戏的 `Mods/`；
3. 启动游戏，读档或开新档。

当前 `Grant.Run()` 默认只发放一个 **海蓝宝石成品展示（2×2）** 用于看效果；
机器、晶核、测试水的批量发放保留在 `GrantBulk()`，需要时取消 `Run` 里的注释。

## 目录

```
src/                     当前 GemBasic 内容包/补丁源码
  ItemForge.items.json   物品定义（59+ 件）
  icons/                 预染色图标 + 白模晶核
  PatchEndNight.cs       生长/重量/颜色/杂质逻辑
  PatchBeginDay.cs       发放与本地化注入
  PatchRegisterMachine.cs 自建结晶槽窗口
examples/ItemForgePackTemplate/  最小 ItemForge 内容包模板
docs/                    设计/实现/研究文档
```

## 已知限制 / 下一步

- 机器目前只有一台基础结晶槽，高压舱、模组、退火都是设计未实现；
- 原石、鉴定、切割、首饰、商人供货还没接；
- 数值（价格、生长速度、上限）未平衡；
- `GrantBulk` 一次发放太多柜台放不下，默认关闭。

详细现状见 [PROJECT_STATE.md](PROJECT_STATE.md)。
