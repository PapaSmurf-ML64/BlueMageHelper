using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace BlueMageHelper.Windows;

public static class Helper
{
    public const float SeparatorPadding = 1.0f;
    public static float GetSeparatorPaddingHeight => SeparatorPadding * ImGuiHelpers.GlobalScale;

    public static float CalculateChildHeight()
    {
        return ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y + GetSeparatorPaddingHeight;
    }

    public static void DrawScaledIcon(uint iconId, Vector2 iconSize)
    {
        iconSize *= ImGuiHelpers.GlobalScale;
        var texture = Plugin.Texture.GetFromGameIcon(iconId).GetWrapOrDefault();
        if (texture == null)
        {
            ImGui.TextUnformatted($"Unknown icon {iconId}");
            return;
        }

        ImGui.Image(texture.Handle, iconSize);
    }

    public static bool DrawArrows(ref int selected, int length, int id = 0)
    {
        var changed = false;

        // Prevents changing values from triggering EndDisable
        var isMin = selected == 0;
        var isMax = selected + 1 == length;

        ImGui.SameLine();
        using (ImRaii.Disabled(isMin))
        {
            if (ImGuiComponents.IconButton(id, FontAwesomeIcon.ArrowLeft))
            {
                selected--;
                changed = true;
            }
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(isMax))
        {
            if (ImGuiComponents.IconButton(id + 1, FontAwesomeIcon.ArrowRight))
            {
                selected++;
                changed = true;
            }
        }

        return changed;
    }
}