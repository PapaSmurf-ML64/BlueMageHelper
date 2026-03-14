using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace BlueMageHelper.Windows;

// https://github.com/Ottermandias/OtterGui/blob/5a9ca815a981802cf56cb11267a0c3a9aa583d72/Widgets/ClippedSelectableCombo.cs
public class ClippedCombo<T> where T : NumberedItem
{
    private readonly int PushId;
    private readonly string Label;

    private readonly IList<T> Items;
    private readonly Func<T, string> ItemToName;
    private readonly Func<T, string, bool> ItemFilter;

    private string Filter = string.Empty;
    private readonly List<(int num, int idx)> RemainingItems;

    private readonly float PreviewSize;
    private const int ItemsAtOnce = 15;

    public ClippedCombo(string id, string label, float previewSize, IList<T> items, Func<T, string, bool> filterPredicate,
        Func<T, string> itemToName)
    {
        PushId = id.GetHashCode();
        Label = label;
        Items = items;
        ItemFilter = filterPredicate;
        ItemToName = itemToName;
        RemainingItems = items.Select((s, i) => (s.Number, i)).ToList();
        PreviewSize = previewSize;
    }

    private bool DrawList(string currentName, out int selectedIdx)
    {
        selectedIdx = -1;
        var height = ImGui.GetTextLineHeightWithSpacing();
        using var child = ImRaii.Child("##List",
            new Vector2(PreviewSize * ImGuiHelpers.GlobalScale, height * ItemsAtOnce));
        if (!child)
            return false;

        var tmpIdx = selectedIdx;

        void DrawItemInternal((int, int) p)
        {
            var name = ItemToName(Items[p.Item2]);
            if (ImGui.Selectable(name, currentName == name))
                tmpIdx = p.Item2;
        }

        ImGuiClip.ClippedDraw(RemainingItems, DrawItemInternal, height);
        if (tmpIdx == selectedIdx)
            return false;

        selectedIdx = tmpIdx;
        ImGui.CloseCurrentPopup();
        return true;
    }

    public bool Draw(string currentName, out int newIdx, ImGuiComboFlags flags = ImGuiComboFlags.None)
    {
        newIdx = -1;
        using var id = ImRaii.PushId(PushId);
        ImGui.SetNextItemWidth(PreviewSize * ImGuiHelpers.GlobalScale);

        using var combo = ImRaii.Combo(Label, currentName, flags | ImGuiComboFlags.HeightLargest);
        if (!combo)
            return false;

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
            UpdateFilter(string.Empty);
        }

        ImGui.SetNextItemWidth(-1);
        var tmp = Filter;
        var enter = ImGui.InputTextWithHint("##filter", "Search...", ref tmp, 255,
            ImGuiInputTextFlags.EnterReturnsTrue);
        UpdateFilter(tmp);

        if (enter && RemainingItems.Count == 0)
        {
            ImGui.CloseCurrentPopup();
            return false;
        }

        var isFocused = ImGui.IsItemFocused();

        var ret = DrawList(currentName, out newIdx);
        if (ret)
            return true;

        if (!enter && (isFocused || RemainingItems.Count != 1))
            return false;

        newIdx = RemainingItems[0].Item2;
        ImGui.CloseCurrentPopup();
        return true;
    }

    public bool Draw(int currentIdx, out int newIdx, ImGuiComboFlags flags = ImGuiComboFlags.None)
    {
        var ret = false;
        if (currentIdx < 0 || currentIdx >= Items.Count)
        {
            currentIdx = 0;
            newIdx = currentIdx;
            ret = true;
        }

        var name = Items.Count > 0 ? ItemToName(Items[currentIdx]) : string.Empty;
        return Draw(name, out newIdx, flags) || ret;
    }

    private void UpdateFilter(string newFilter)
    {
        if (newFilter == Filter)
            return;

        var newLower = newFilter.ToLowerInvariant();
        var lower = Filter.ToLowerInvariant();

        if (Filter.Length > 0 && newLower.Contains(lower))
        {
            for (var i = 0; i < RemainingItems.Count; ++i)
            {
                if (ItemFilter(Items[RemainingItems[i].Item2], newLower))
                    continue;

                RemainingItems.RemoveAt(i--);
            }
        }
        else if (newLower.Length > 0)
        {
            RemainingItems.Clear();
            for (var i = 0; i < Items.Count; ++i)
            {
                if (ItemFilter(Items[i], newLower))
                    RemainingItems.Add((Items[i].Number, i));
            }
        }
        else
        {
            RemainingItems.Clear();
            for (var i = 0; i < Items.Count; ++i)
                RemainingItems.Add((Items[i].Number, i));
        }

        Filter = newFilter;
    }
}

public abstract record NumberedItem
{
    public required int Number;
}