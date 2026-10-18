# ItemForge 内容包模板（PSItemPack_Demo）

这是给《Probably Stolen》demo 用的 **ItemForge 内容包** 脚手架：纯数据驱动，不改游戏本体，
编译出一个 `PSItemPack_Demo.dll` 丢进 `Mods/` 即可。前提是游戏已装 `ItemForgeRuntime.dll`
（本机已有）。

## 目录结构

```
ItemForgePackTemplate/
├─ PSItemPack_Demo.csproj        # 目标 net6.0 / x64，引用 MelonLoader，嵌入 JSON+PNG
├─ AssemblyInfo.cs               # MelonInfo / MelonGame / MelonAdditionalDependencies
├─ ContentPackMod.cs             # 空的 MelonMod（让 MelonLoader 加载本 DLL）
├─ GeneratedPackInfo.cs          # 包元数据常量（作者等）
├─ PackMarker.cs                 # RequiredRuntimeApi = 20
├─ ItemForge.items.json          # ★ 物品/机器定义（核心）
├─ manifest.xml                  # 给外部工具（Item Manager）看的元数据
└─ icons/                        # 每个物品一张 PNG，文件名 = 物品 Id
   ├─ demo_berry.png             #   1x1 网格 → 16x16 px
   ├─ demo_jam.png               #   2x2 网格 → 32x32 px
   └─ demo_slow_cooker.png       #   3x3 网格 → 48x48 px
```

## 编译

```bash
dotnet build -c Release
# 产物：bin/Release/net6.0/PSItemPack_Demo.dll
```

如果游戏不在默认目录，覆盖 `GameDir`：

```bash
dotnet build -c Release -p:GameDir="E:\path\to\Probably Stolen Demo"
```

## 安装

把 `bin/Release/net6.0/PSItemPack_Demo.dll` 复制到游戏根的 `Mods/` 目录即可
（与 `ItemForgeRuntime.dll` 同级）。**文件名必须以 `PSItemPack_` 开头**，运行时靠这个前缀发现内容包。

## 修改内容

* **加 / 改物品**：编辑 `ItemForge.items.json`。字段含义见生成的研究报告。
* **加图标**：往 `icons/` 放 `<物品Id>.png`，csproj 会自动以 `ItemForge.icons.<Id>.png` 的名字嵌入。
* **加新机器**：复制示例里的 `demo_slow_cooker` 那段，`DirectoryKind` 用 `"Container"`，
  `Types` 带 `"PROCESSING_MACHINE"`，`ProcessingMachineEnabled: true`，在
  `ProcessingMachineRecipes` 里写「输入物品 → 输出物品 / 天数」。

## 注意

* `RequiredRuntimeApi` 必须 ≤ 当前 `ItemForgeRuntime` 的 API 版本（现为 **20**），否则整包被跳过。
* 图标缺失不会崩，只是没图。
* 本包只负责「把物品注册进游戏」。**怎么让玩家拿到**（商店/掉落/搜刮）要么用 ItemForge 的
  `ScavengeBonus*` / `CustomerDrop*` 字段，要么走独立 mod 路线（见研究报告）。
