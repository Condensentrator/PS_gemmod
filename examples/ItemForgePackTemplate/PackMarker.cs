// Declares the ItemForge runtime API level this pack was built against.
// Must match (or be <=) ItemRuntime.RuntimeApiVersion in the installed
// ItemForgeRuntime.dll, which is 20 for the current build of the game.
namespace ItemForge.ContentPack
{
    public static class PackMarker
    {
        public const int RequiredRuntimeApi = 20;
    }
}
