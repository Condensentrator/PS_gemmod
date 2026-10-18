using MelonLoader;

// The empty MelonMod subclass simply makes MelonLoader load this DLL.
// All the real work (reading the embedded JSON + injecting items) is done by
// ItemForgeRuntime, which discovers this pack by its file name prefix PSItemPack_*.
namespace ItemForge.ContentPack
{
    public class ContentPackMod : MelonMod
    {
    }
}
