using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using static BlueMageHelper.SpellSources;

namespace BlueMageHelper.Windows;

public class MainWindow : Window, IDisposable
{
    private Plugin Plugin;

    /// <summary>
    /// Index of the selected spell, NOT the spell id
    /// </summary>
    public int SelectedIndex;

    private int SelectedSource;
    private static readonly Vector2 IconSize = new(110, 110);
    public List<BlueSpell>? AllBlueSpells;


    public record BlueSpell
    {
        public required string Name;
        public required string Description;
        public int Number;
        public uint IconId;
    }

    public ClippedCombo<BlueSpell>? SpellSelector;

    public MainWindow(Plugin plugin) : base("Grimoire##BlueMageHelper")
    {
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(372, 500),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog,
            ShowTooltip = () => ImGui.SetTooltip("Open Config"),
            Click = (_) => Plugin?.ConfigWindow.Toggle(),
        });

        Plugin = plugin;
        SelectedIndex = 0;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var itemSpacing = (float)(ImGui.GetStyle().ItemSpacing.Y * Math.Pow(ImGuiHelpers.GlobalScale, 1.8));
        if (AllBlueSpells == null || SpellSelector == null)
        {
            AllBlueSpells = Plugin.AozActionsCache
                .Select(a => new BlueSpell
                {
                    Name = a.Action.Value.Name.ToString(),
                    Description = Plugin.ActionTransient.GetRow(a.Action.RowId).Description.ToString(),
                    Number = Plugin.AozTransientCache[(int)a.RowId - 1].Number,
                    IconId = Plugin.AozTransientCache[(int)a.RowId - 1].Icon
                })
                .Where(a => IsUnlocked(a.Number))
                .OrderBy(a => a.Number)
                .ToList();

            SpellSelector = new ClippedCombo<BlueSpell>("BlueSpellSelector",
                string.Empty,
                275,
                AllBlueSpells,
                g => $"{g.Name} (#{g.Number})");
        }

        var currentSpell = SelectedIndex;

        using (ImRaii.Disabled(AllBlueSpells.Count == 0))
        {
            if (SpellSelector.Draw(SelectedIndex, out int selectedSpellIndex))
            {
                SelectedIndex = selectedSpellIndex;
            }
        }

        SelectedIndex = SelectedIndex < 0 ? 0 : SelectedIndex;

        Helper.DrawArrows(ref SelectedIndex, AllBlueSpells.Count, 0, itemSpacing);
        ImGui.SameLine(0, itemSpacing);
        if (ImGui.Checkbox("##unlearnedSpells", ref Plugin.Configuration.ShowOnlyUnlearned))
        {
            SpellSelector = null;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show only unlearned spells.");

        if (AllBlueSpells.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.ParsedOrange, "All spells learned, nothing to show!");
            ImGui.TextColored(ImGuiColors.ParsedOrange, "[Disable 'Show only unlearned' if you want to see them.]");
            return;
        }

        if (currentSpell != SelectedIndex)
            SelectedSource = 0;

        ImGuiHelpers.ScaledDummy(5);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5);

        var scaledIconSize = IconSize * ImGuiHelpers.GlobalScale;
        var screenCursor = ImGui.GetCursorScreenPos();

        var rectPos = new Vector2(screenCursor.X, screenCursor.Y);
        var rectSize = new Vector2(
            x: ImGui.GetContentRegionMax().X,
            y: scaledIconSize.Y + ImGui.GetStyle().ItemSpacing.Y + 10
        );

        var rectColor = ImGui.GetColorU32(ImGuiCol.TableHeaderBg);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(rectPos, rectPos + rectSize, rectColor);

        if (!Spells.Any())
        {
            Services.Log.Warning("No spells found.");
            return;
        }

        if (!Spells.TryGetValue(AllBlueSpells[SelectedIndex].Number, out var selectedSpell))
        {
            Services.Log.Warning($"Selected spell {SelectedIndex} not found.");
            ImGui.TextColored(ImGuiColors.DalamudYellow, $"Render Error.");
            return;
        }

        ImGui.Indent(7);
        var cursor = ImGui.GetCursorPos();

        ImGui.SetCursorPosY(cursor.Y + 7);
        Helper.DrawScaledIcon(AllBlueSpells[SelectedIndex].IconId, IconSize);
        screenCursor = ImGui.GetCursorScreenPos();
        cursor = ImGui.GetCursorPos();

        var learnedMarkerSize = new Vector2(25, 25) * ImGuiHelpers.GlobalScale;
        var learnedMarkerBgSize = 40 * ImGuiHelpers.GlobalScale;

        var p1 = scaledIconSize with { Y = -4 };
        var p2 = p1 with { Y = p1.Y - learnedMarkerBgSize };
        var p3 = p1 with { X = p1.X - learnedMarkerBgSize };

        drawList.AddTriangleFilled(screenCursor + p1,
            screenCursor + p2,
            screenCursor + p3,
            ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)));

        ImGui.SetCursorPos(cursor + p1 + (-1 * learnedMarkerSize));
        var exists = Plugin.UnlockedSpells.TryGetValue(AllBlueSpells[SelectedIndex].Number, out var unlocked);
        DrawUnlockSymbol(exists && unlocked, learnedMarkerSize);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(unlocked ? "Spell learned" : "Spell not learned");
        cursor = ImGui.GetCursorPos();
        ImGui.SetCursorPos(cursor + new Vector2(scaledIconSize.X + 7, -(scaledIconSize.Y + 7)));
        using (ImRaii.Child("blueDescription", new Vector2(0, scaledIconSize.Y + 7)))
        {
            ImGui.TextWrapped(AllBlueSpells[SelectedIndex].Description);
        }

        ImGui.SetCursorPos(cursor);
        ImGui.Unindent(7);
        ImGuiHelpers.ScaledDummy(10);

        using (var contentChild = ImRaii.Child("Content", new Vector2(0, -30)))
        {
            if (contentChild.Success)
            {
                var source = selectedSpell.Sources[SelectedSource];
                if (selectedSpell.HasMultipleSources)
                {
                    var sourcesList = selectedSpell.Sources
                        .Select(x => $"{x.Type.GetDisplay()}: " + (x.IsDuty ? x.DutyName : x.PlaceName))
                        .ToArray();

                    ImGui.Combo("##sourcesSelector", ref SelectedSource, sourcesList, sourcesList.Length);

                    Helper.DrawArrows(ref SelectedSource, selectedSpell.Sources.Count, 2);
                    source = selectedSpell.Sources[SelectedSource];
                    ImGui.TextWrapped($"Target: {(source.Type == RegionType.Unknown ? "Currently Unknown" : source.Info)}");
                }
                else
                {
                    ImGui.Text(source.Type != RegionType.Unknown
                        ? $"{source.Type.GetDisplay()}: {source.Info}"
                        : "Currently Unknown");
                }

                switch (source.Type)
                {
                    case RegionType.ARank:
                        ImGui.Text($"Note: Rank A Elite Mark");
                        break;
                    case RegionType.BRank:
                        ImGui.Text($"Note: Rank B Elite Mark");
                        break;
                    case RegionType.SRank:
                        ImGui.Text($"Note: Rank S Elite Mark");
                        break;
                }

                if (source.Type != RegionType.Buy && source.DutyMinLevel != "1")
                    ImGui.Text($"Min Lvl: {source.DutyMinLevel}");

                ImGuiHelpers.ScaledDummy(4);

                if (source is { TerritoryType: not null, IsDuty: true })
                {
                    if (source.Type != RegionType.MaskedCarnivale)
                    {
                        DrawOpenDutyButton(source.TerritoryTypeID);
                        ImGui.SameLine();
                    }

                    ImGui.TextUnformatted(
                        $"{(source.Type == RegionType.MaskedCarnivale ? "Masked Carnivale" : "Duty")}: " +
                        $"{source.DutyName}"
                    );
                }

                var isHunt = source.Type is RegionType.ARank or RegionType.BRank or RegionType.SRank;
                if (source.MapLink != null || isHunt)
                {
                    if (Plugin.TeleportConsumer.IsAvailable)
                    {
                        using var font = ImRaii.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button($"{FontAwesomeIcon.StreetView.ToIconString()}"))
                            Plugin.TeleportToNearestAetheryte(source);
                        font.Pop();
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Teleport to nearest aetheryte");
                        ImGui.SameLine();
                    }

                    if (source.MapLink != null)
                    {
                        using var font = ImRaii.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button($"{FontAwesomeIcon.MapMarkedAlt.ToIconString()}"))
                            Plugin.SetMapMarker(source.MapLink);
                        font.Pop();
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Place map flag");
                        ImGui.SameLine();
                    }

                    if (source.CurrentlyUnknown)
                    {
                        ImGui.Text("Location: ???");
                    }
                    else
                    {
                        var text = isHunt
                            ? source.PlaceName
                            : $"{source.PlaceName} {source.MapLink?.CoordinateString ?? "???"}";
                        ImGui.Text($"Location: {text}");
                    }
                }

                if (source.TerritoryType != null && source.Type != RegionType.Buy)
                {
                    var combos = Spells
                        .Where(key => key.Key != AllBlueSpells[SelectedIndex].Number)
                        .Where(spell => spell.Value.Sources.Any(spellSource => source.CompareTerritory(spellSource)))
                        .ToArray();

                    if (combos.Length != 0)
                    {
                        ImGuiHelpers.ScaledDummy(5);
                        ImGui.Separator();
                        ImGuiHelpers.ScaledDummy(5);
                        ImGui.Text("Same location:");
                        foreach (var (key, value) in combos)
                        {
                            using (ImRaii.PushFont(UiBuilder.IconFont))
                            {
                                ImGui.Text($"{FontAwesomeIcon.ArrowRightArrowLeft.ToIconString()}");
                                ImGui.SameLine();
                            }

                            if (ImGui.Selectable($"#{key} - {value.Name}"))
                            {
                                SelectedSource = value.Sources
                                    .FindIndex(x => x.TerritoryTypeID == source.TerritoryTypeID);
                                SelectedIndex = AllBlueSpells
                                    .FindIndex(val => val.Number == key);
                            }
                            // drawList.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                            //     ImGui.GetColorU32(ImGuiCol.Border), 0, 0, 1);
                        }
                    }
                }

                if (source.AcquiringTips != "")
                {
                    ImGuiHelpers.ScaledDummy(5);
                    ImGui.Separator();
                    ImGuiHelpers.ScaledDummy(5);

                    if (ImGui.CollapsingHeader($"Acquisition Tips##{selectedSpell.Icon}"))
                    {
                        foreach (var tip in source.AcquiringTips.Split("\n"))
                        {
                            ImGui.Bullet();
                            using (ImRaii.TextWrapPos(0.0f))
                                ImGui.Text(tip);
                        }
                    }
                }
            }
        }

        using (var bottomBar = ImRaii.Child("BottomBar", new Vector2(0, 0)))
        {
            if (bottomBar.Success)
                ImGui.TextDisabled("Data sourced from ffxiv.consolegameswiki.com");
        }
    }

    private static unsafe void DrawOpenDutyButton(uint territoryType)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        if (ImGui.Button(FontAwesomeIcon.ArrowUpRightFromSquare.ToIconString()))
        {
            var territory = Plugin.TerritorySheet.GetRowOrDefault(territoryType);
            if (territory.HasValue)
            {
                AgentContentsFinder.Instance()->OpenRegularDuty(territory.Value
                    .ContentFinderCondition
                    .RowId);
            }
        }

        font.Pop();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Open in duty finder");
    }

    private static void DrawUnlockSymbol(bool done, Vector2 size)
    {
        var color = done ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
        if (Services.TextureProvider.GetFromGameIcon(done ? 60081 : 61552)
            .TryGetWrap(out var texture, out var exception))
            ImGui.Image(texture.Handle, size, color);
    }

    private bool IsUnlocked(int num) => !Plugin.Configuration.ShowOnlyUnlearned ||
                                        (Plugin.UnlockedSpells.TryGetValue(num, out var isUnlocked) && !isUnlocked);
}