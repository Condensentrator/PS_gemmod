using MelonLoader;
using ItemForge.ContentPack;

[assembly: MelonInfo(typeof(ContentPackMod), "示例内容包", "1.0.0", "你的名字", null)]
[assembly: MelonGame("Questing Goose Studio", "Probably Stolen")]

// Force ItemForgeRuntime to initialize BEFORE this pack, so its item
// directories are patched before the pack DLL is scanned.
[assembly: MelonAdditionalDependencies("ItemForgeRuntime")]
