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
using Lumina.Excel.Sheets;
using static BlueMageHelper.SpellSources;
using MapType = FFXIVClientStructs.FFXIV.Client.UI.Agent.MapType;

namespace BlueMageHelper.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin Plugin;

    /// <summary>
    /// Index of the selected spell, NOT the spell id
    /// </summary>
    public int SelectedIndex;

    public int SelectedSource;
    private static readonly Vector2 IconSize = new(110, 110);
    public List<BlueSpell>? AllBlueSpells;


    public record BlueSpell : NumberedItem
    {
        public required string Name;
        public required string Description;
        public required string Location;
        public uint IconId;

        public static bool SearchPredicate(BlueSpell spell, string search)
        {
            return spell.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                   spell.Location.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                   Int16.TryParse(search, out var id) && spell.Number == id;
        }
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
                    Location = string.Join("|",Spells[Plugin.AozTransientCache[(int)a.RowId - 1].Number].Sources.Select(s=>s.PlaceName)),
                    Number = Plugin.AozTransientCache[(int)a.RowId - 1].Number,
                    IconId = Plugin.AozTransientCache[(int)a.RowId - 1].Icon
                })
                .Where(a => Plugin.IsSpellUnlocked(a.Number))
                .OrderBy(a => a.Number)
                .ToList();

            SpellSelector = new ClippedCombo<BlueSpell>("BlueSpellSelector",
                string.Empty,
                275,
                AllBlueSpells,
                BlueSpell.SearchPredicate,
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
        var arrowSize = ImGui.GetItemRectSize();
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

        // if (Services.TargetManager.Target is not null && Services.TargetManager.Target?.Address != 0)
        // {
        //     ImGui.Text($"Target: '{Services.TargetManager.Target?.Name}'\t base_id:{Services.TargetManager.Target?.BaseId}");
        // }

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
                SelectedSource = Math.Clamp(SelectedSource, 0, selectedSpell.Sources.Count - 1);
                var source = selectedSpell.Sources[SelectedSource];
                if (selectedSpell.HasMultipleSources)
                {
                    var sourcesList = selectedSpell.Sources
                        .Select(x => $"{x.Type.GetDisplayName()}: " + (x.IsDuty ? x.DutyName : x.PlaceName))
                        .ToArray();

                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (arrowSize.X * 2 + itemSpacing * 2));
                    ImGui.Combo("##sourcesSelector", ref SelectedSource, sourcesList, sourcesList.Length);

                    Helper.DrawArrows(ref SelectedSource, selectedSpell.Sources.Count, 2, itemSpacing);
                    source = selectedSpell.Sources[SelectedSource];
                    ImGui.TextWrapped($"Target: {(source.Type == RegionType.Unknown ? "Currently Unknown" : source.Info)}");
                }
                else
                {
                    ImGui.Text(source.Type != RegionType.Unknown
                        ? $"{source.Type.GetDisplayName()}: {source.Info}"
                        : "Currently Unknown");
                }

                // switch (source.Type)
                // {
                //     case RegionType.ARank:
                //         ImGui.Text($"Note: Rank A Elite Mark");
                //         break;
                //     case RegionType.BRank:
                //         ImGui.Text($"Note: Rank B Elite Mark");
                //         break;
                //     case RegionType.SRank:
                //         ImGui.Text($"Note: Rank S Elite Mark");
                //         break;
                // }

                // if (source.TerritoryTypeId != 0)
                //     ImGui.Text($"Location: {source.Location[0]}, {source.Location[1]}");

                if (source.Type != RegionType.Buy && source.DutyMinLevel != "1" || source.LevelMin != 0)
                    ImGui.Text($"Min Level: {source.DutyMinLevel}");

                ImGuiHelpers.ScaledDummy(4);

                var isHunt = source.Type is RegionType.ARank or RegionType.BRank or RegionType.SRank;
                if (source.HasValidLocation || isHunt)
                {
                    if (source is { IsDuty: false })
                    {
                        using var font = ImRaii.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button($"{FontAwesomeIcon.StreetView.ToIconString()}", new Vector2(35, 36)))
                            Plugin.TeleportToNearestAetheryte(source);
                        font.Pop();
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Teleport to nearest aetheryte");
                        ImGui.SameLine();
                    }
                    else if (source is { IsDuty: true } && source.Type != RegionType.MaskedCarnivale)
                    {
                        DrawOpenDutyButton(source);
                        ImGui.SameLine();
                    }

                    if (source.HasValidLocation)
                    {
                        using var font = ImRaii.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button($"{FontAwesomeIcon.MapMarkedAlt.ToIconString()}", new Vector2(35, 36)))
                            PlaceMapMarker(source);
                        font.Pop();
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Place map marker");
                        ImGui.SameLine();
                    }

                    var display = source.Type switch
                    {
                        RegionType.ARank or RegionType.BRank or RegionType.SRank or RegionType.OpenWorld =>
                            $"Zone: {source.PlaceName} {source.MapLink?.CoordinateString}",
                        RegionType.Dungeon or RegionType.Trial or RegionType.Raid =>
                            $"{source.Type.GetDisplayName()}: {source.DutyName}",
                        RegionType.MaskedCarnivale => $"{source.Type.GetDisplayName()}: {source.DutyName}",
                        RegionType.Unknown => "Currently Unknown",
                        _ => $"{source.Type.GetDisplayName()}"
                    };

                    if (ImGui.Selectable(display))
                    {
                        PlaceMapMarker(source);
                    }

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(source.PlaceName);
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
                                    .FindIndex(x => x.TerritoryTypeId == source.TerritoryTypeId);
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

    // From MapLinkPayload
    private static int ConvertMapCoordinateToRawPosition(float pos, float scale, short offset)
    {
        const float networkAdjustment = 1f;

        // scaling
        var trueScale = scale / 100f;

        var num2 = (((pos - networkAdjustment) * trueScale / 41f * 2048f) - 1024f) / trueScale;
        // (pos - offset) / scale, with the scaling on num2 done before for precision
        num2 *= 1000f;
        return (int)num2 - (offset * 1000);
    }

    private static (int X, int Y) MapToWorldCoordinates(Vector2 pos, Map map)
    {
        var x1 = ConvertMapCoordinateToRawPosition(pos.X, map.SizeFactor, map.OffsetX) / 1000;
        var y1 = ConvertMapCoordinateToRawPosition(pos.Y, map.SizeFactor, map.OffsetY) / 1000;
        return (x1, y1);
    }

    private unsafe void PlaceMapMarker(SpellSource src)
    {
        var instance = AgentMap.Instance();

        if (src.Location[0] == 0 && src.Location[1] == 0)
        {
            if (src.TerritoryType != null)
                instance->OpenMapByMapId(src.MapId);
            return;
        }

        if (src is { TerritoryType: { } terr, Map: { } map })
        {
            var sizeFactor = map.SizeFactor / 100f;
            var (x1, y2) = MapToWorldCoordinates(new Vector2(src.Location[0], src.Location[1]), map);
            // Services.Log.Info($"Placing map marker for {src.Info} {src.Location[0]}, {src.Location[1]}");
            // Services.Log.Info($" terr={terr.RowId} map={map.RowId} radius={src.Radius}");
            instance->TempMapMarkerCount = 0;
            // instance->FlagMarkerCount = 0;
            instance->AddGatheringTempMarker(x1, y2, src.Radius, 94129, 4u, src.Info);
            // instance->SetFlagMapMarker(tt.RowId, tt.Map.RowId, x, y);
            instance->OpenMap(map.RowId, terr.RowId, src.Info, MapType.QuestLog);
        }
    }

    private static unsafe void DrawOpenDutyButton(SpellSource src)
    {
        using var font = ImRaii.PushFont(UiBuilder.IconFont);
        if (GameIconButton(src.Type.GetDisplayIcon()))
        {
            var territory = Plugin.TerritorySheet.GetRowOrDefault(src.TerritoryTypeId);
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

    public static bool GameIconButton(uint iconId)
    {
        var iconTexture = Services.TextureProvider.GetFromGameIcon(iconId);
        return ImGui.ImageButton(iconTexture.GetWrapOrEmpty().Handle, new Vector2(31, 31));
    }
}