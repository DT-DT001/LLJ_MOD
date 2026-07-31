using Gameplay;
using HarmonyLib;
using Player;
using UI;

namespace LLJDeviceLink;

internal static class InputBlockState
{
    public static bool Active;
}

// The mod window is IMGUI and the game uses a mixture of its own input manager,
// direct camera polling and Unity EventSystem UI. These targeted guards stop the
// game-side consumers while leaving the mod's native click handling operational.
[HarmonyPatch(typeof(InputControlManager), nameof(InputControlManager.GetSimpleKeyIsDown))]
internal static class BlockSimpleKeyDown
{
    private static bool Prefix(ref bool __result)
    {
        if (!InputBlockState.Active) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(InputControlManager), nameof(InputControlManager.GetSimpleKeyIsUp))]
internal static class BlockSimpleKeyUp
{
    private static bool Prefix(ref bool __result)
    {
        if (!InputBlockState.Active) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(InputControlManager), nameof(InputControlManager.GetSimpleKeyIsPressed))]
internal static class BlockSimpleKeyPressed
{
    private static bool Prefix(ref bool __result)
    {
        if (!InputBlockState.Active) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(InputControlManager), nameof(InputControlManager.GetSimpleKeyPressingTime))]
internal static class BlockSimpleKeyPressingTime
{
    private static bool Prefix(ref float __result)
    {
        if (!InputBlockState.Active) return true;
        __result = 0f;
        return false;
    }
}

[HarmonyPatch(typeof(TaskPlayerControl), nameof(TaskPlayerControl.HandleInteract))]
internal static class BlockPlayerInteract
{
    private static bool Prefix(ref bool __result)
    {
        if (!InputBlockState.Active) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(TaskPlayerControl), nameof(TaskPlayerControl.HandleOperableTool))]
internal static class BlockOperableTool
{
    private static bool Prefix(ref bool __result)
    {
        if (!InputBlockState.Active) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(ItemManager), nameof(ItemManager.HandleSwitchOperableItemInput))]
internal static class BlockItemMouseWheel
{
    private static bool Prefix() => !InputBlockState.Active;
}

[HarmonyPatch(typeof(RsCameraControl), "Update")]
internal static class BlockCameraMouseInput
{
    private static bool Prefix() => !InputBlockState.Active;
}

[HarmonyPatch(typeof(objectInteraction), "OnMouseUp")]
internal static class BlockLegacyWorldMouseClick
{
    private static bool Prefix() => !InputBlockState.Active;
}
