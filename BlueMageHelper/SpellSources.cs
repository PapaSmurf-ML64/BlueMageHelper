using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;

namespace BlueMageHelper;

public class Spell
{
    public string? Name;
    public uint Icon;
    public readonly List<SpellSource> Sources = [];

    public Spell() { }

    [JsonIgnore] public SpellSource Source => Sources[0];
    [JsonIgnore] public bool HasMultipleSources => Sources.Count > 1;
}

public class SpellSource
{
    public string? Info;
    public string AcquiringTips = "";

    public RegionType Type = RegionType.Default;
    public uint TerritoryTypeID = 0;
    public uint? ContentFinderCondition = 0;
    public float xCoord = 0;
    public float yCoord = 0;

    [NonSerialized] public TerritoryType? TerritoryType = null;
    [NonSerialized] public MapLinkPayload? MapLink = null;

    [NonSerialized] public bool IsDuty = false;
    [NonSerialized] public string DutyName = "";
    [NonSerialized] public string DutyMinLevel = "1";
    [NonSerialized] public string PlaceName = "";

    [NonSerialized] public bool CurrentlyUnknown = false;

    public SpellSource() { }

    public SpellSource(string info)
    {
        Info = info;
    }

    public SpellSource(string info, RegionType type)
    {
        Info = info;
        Type = type;
    }

    [OnDeserialized]
    public void Initialize(StreamingContext _)
    {
        if (TerritoryTypeID != 0 && Plugin.TerritorySheet.HasRow(TerritoryTypeID))
        {
            TerritoryType = Plugin.TerritorySheet.GetRow(TerritoryTypeID);
            PlaceName = TerritoryType.Value.PlaceName.Value.Name.ExtractText();

            var content = Type is RegionType.MaskedCarnivale
                ? Plugin.ContentFinderSheet.FirstOrNull(c => c.RowId == ContentFinderCondition)
                : Plugin.ContentFinderSheet.FirstOrNull(c =>
                    c.TerritoryType.RowId == TerritoryType.Value.RowId);
            if (content != null && content.Value.Name.ExtractText() != "")
            {
                IsDuty = true;
                DutyName = Utils.ToTitleCaseExtended(content.Value.Name);
                DutyMinLevel = content.Value.ClassJobLevelRequired.ToString();
                if (Type is RegionType.MaskedCarnivale)
                {
                    DutyName = $"{content.Value.ShortCode.ToString()[^2..].TrimStart('0')} - {DutyName}";
                }
            }
        }

        if (Type == RegionType.OpenWorld && TerritoryType != null)
        {
            if (xCoord == 0 || yCoord == 0)
            {
                CurrentlyUnknown = true;
                MapLink = new MapLinkPayload(TerritoryType.Value.RowId, TerritoryType.Value.Map.RowId, 15, 15);
                return;
            }

            try
            {
                MapLink = new MapLinkPayload(TerritoryType.Value.RowId, TerritoryType.Value.Map.RowId, xCoord, yCoord);
            }
            catch
            {
                Services.Log.Error($"MapLink creation failed for {Info}.");
            }
        }
        else if (Type == RegionType.Buy)
        {
            // Ul'dah - Steps of Thal - (x12.5, y12.9)
            TerritoryType = Plugin.TerritorySheet.GetRow(131);
            PlaceName = TerritoryType.Value.PlaceName.Value.Name.ExtractText();
            MapLink = new MapLinkPayload(TerritoryType.Value.RowId, TerritoryType.Value.Map.RowId, 12.5f, 12.9f);
        }
    }

    public bool CompareTerritory(SpellSource other)
    {
        if (other.TerritoryType == null || TerritoryType == null)
            return false;

        return other.TerritoryType.Value.RowId == TerritoryType.Value.RowId;
    }

    public unsafe void SetRegion(AtkTextNode* region, AtkImageNode* regionType)
    {
        var text = Type switch
        {
            RegionType.OpenWorld => $"{MapLink!.PlaceName} {MapLink!.CoordinateString}",
            RegionType.Buy => $"{MapLink!.PlaceName} {MapLink!.CoordinateString}",
            RegionType.Dungeon => $"{TerritoryType?.PlaceName.Value.Name.ExtractText() ?? "Unknown"}",
            _ => ""
        };

        if (text != "") region->SetText(text);
        if (Type != RegionType.Default) regionType->PartId = (ushort)Type;
    }
}

// ID = PartID
public enum RegionType
{
    [Display("Mob")] OpenWorld = 2,
    [Display("Totem Purchase")] Buy = 3,
    [Display("Dungeon")] Dungeon = 13,
    [Display("Fate Mob")] Fate = 26,

    // non PartIDs
    [Display("Info")] Default = 99,
    [Display("A Rank")] ARank = 100,
    [Display("B Rank")] BRank = 101,
    [Display("S Rank")] SRank = 102,

    [Display("Masked Carnivale")] MaskedCarnivale = 103,
    [Display("Raid")] Raid = 104,
    [Display("Trial")] Trial = 105,

    // New Patch
    Unknown = 999,
}

[AttributeUsage(AttributeTargets.Field)]
public class DisplayAttribute : Attribute
{
    internal DisplayAttribute(string name)
    {
        DisplayName = name;
    }

    public string DisplayName { get; }
}

public static class RegionTypeDisplayExtension
{
    public static string GetDisplay(this RegionType region)
    {
        var a = region.GetAttribute<DisplayAttribute>();
        return a == null ? "" : a.DisplayName;
    }
}

public static class SpellSources
{
    public static Dictionary<int, Spell> Spells = new();
}