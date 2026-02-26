using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Timers;
using BlueMageHelper.IPC;
using BlueMageHelper.Windows;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using static BlueMageHelper.SpellSources;
using Map = Lumina.Excel.Sheets.Map;

namespace BlueMageHelper;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/spellbook";

    public static Configuration Configuration = null!;
    private WindowSystem WindowSystem = new("Blue Mage Helper");
    public MainWindow MainWindow;
    public ConfigWindow ConfigWindow;

    private string lastSeenSpell = string.Empty;
    private string lastOrgText = string.Empty;

    public List<AozAction> AozActionsCache;
    public List<AozActionTransient> AozTransientCache;

    private bool OnCooldown;
    private readonly Timer Cooldown = new(3 * 1000);

    public static readonly Dictionary<int, bool> UnlockedSpells = new();

    public class TerritoryNpcRecords
    {
        public readonly List<uint> NpcIds = [];
        public readonly List<SpellSource> SpellSources = [];
    }

    public static readonly Dictionary<uint, TerritoryNpcRecords> MobsByTerritory = new();

    public static ExcelSheet<Aetheryte> AetheryteSheet = null!;
    public static ExcelSheet<TerritoryType> TerritorySheet = null!;
    public static ExcelSheet<Map> MapSheet = null!;
    public static SubrowExcelSheet<MapMarker> MapMarkerSheet = null!;
    public static ExcelSheet<ContentFinderCondition> ContentFinderSheet = null!;
    public static ExcelSheet<ActionTransient> ActionTransient = null!;

    public static TeleportConsumer TeleportConsumer = null!;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        AozActionsCache = Services.DataManager.GetExcelSheet<AozAction>().Where(a => a.Rank != 0).ToList();
        AozTransientCache = Services.DataManager.GetExcelSheet<AozActionTransient>().Where(a => a.Number != 0).ToList();

        AetheryteSheet = Services.DataManager.GetExcelSheet<Aetheryte>();
        TerritorySheet = Services.DataManager.GetExcelSheet<TerritoryType>();
        MapSheet = Services.DataManager.GetExcelSheet<Map>();
        MapMarkerSheet = Services.DataManager.GetSubrowExcelSheet<MapMarker>();
        ContentFinderSheet = Services.DataManager.GetExcelSheet<ContentFinderCondition>();
        ActionTransient = Services.DataManager.GetExcelSheet<ActionTransient>();

        TeleportConsumer = new TeleportConsumer();

        Cooldown.AutoReset = false;
        Cooldown.Elapsed += (_, __) => OnCooldown = false;

        Configuration = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        MainWindow = new MainWindow(this);
        ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ConfigWindow);

        Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens a small guide book"
        });

        Services.PluginInterface.UiBuilder.Draw += DrawUi;
        Services.PluginInterface.UiBuilder.OpenMainUi += DrawMainUi;
        Services.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUi;
        Services.Framework.Update += AozNotebookAddonManager;
        Services.Framework.Update += CheckLearnedSpells;

        NamePlateMarkerService.Init();

        try
        {
            var path = Path.Combine(Services.PluginInterface.AssemblyLocation.Directory?.FullName!, "spells.json");
            var jsonString = File.ReadAllText(path);
            Spells = JsonConvert.DeserializeObject<Dictionary<int, Spell>>(jsonString)!;
            foreach (var kvp in Spells)
            {
                kvp.Value.Number = kvp.Key;
                foreach (var src in kvp.Value.Sources)
                {
                    src.Spell = kvp.Value;

                    if (!MobsByTerritory.TryGetValue(src.TerritoryTypeId, out var record))
                    {
                        record = new TerritoryNpcRecords();
                        MobsByTerritory[src.TerritoryTypeId] = record;
                    }

                    if (src.NpcId is { } ids && kvp.Value is not null && !IsSpellUnlocked(kvp.Key))
                    {
                        record.NpcIds.AddRange(ids.Select(i => (uint)i));
                        record.SpellSources.Add(src);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Services.Log.Error("There was a problem building the Grimoire.");
            Services.Log.Error(e.Message);
            Services.Log.Error(e.StackTrace!);
            if (e.InnerException != null)
            {
                Services.Log.Error(e.InnerException.Message);
                Services.Log.Error(e.InnerException.StackTrace!);
            }
        }

// #if DEBUG
//         this.MainWindow.IsOpen = true;
//         // this.ConfigWindow.IsOpen = true;
// #endif
    }

    private void CheckLearnedSpells(IFramework framework)
    {
        if (OnCooldown)
            return;

        OnCooldown = true;
        Cooldown.Start();

        if (!Services.ClientState.IsLoggedIn)
            return;

        if (Services.ObjectTable.LocalPlayer == null)
            return;

        UnlockedSpells.Clear();

        foreach (var (transient, action) in AozTransientCache.Zip(AozActionsCache).OrderBy(pair => pair.First.Number))
        {
            var unlocked = SpellUnlocked(action.Action.Value.UnlockLink.RowId);
            UnlockedSpells.Add(transient.Number, unlocked);
        }
    }

    private void AozNotebookAddonManager(IFramework framework)
    {
        try
        {
            var addonPtr = Services.GameGui.GetAddonByName("AOZNotebook");
            if (addonPtr == nint.Zero)
                return;
            SpellbookWriter(addonPtr);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Services.Log.Verbose("Blue Mage Helper caught an exception: " + e);
        }
    }

    private unsafe void SpellbookWriter(IntPtr addonPtr)
    {
        AtkUnitBase* spellbookBaseNode = (AtkUnitBase*)addonPtr;
        AtkTextNode* unlearnedTextNode = spellbookBaseNode->GetTextNodeById(68);
        if (!unlearnedTextNode->AtkResNode.IsVisible() && !Configuration.ShowHintEvenIfUnlocked)
            return;

        AtkTextNode* emptyTextnode = spellbookBaseNode->GetTextNodeById(78);
        AtkTextNode* spellNumberTextnode = spellbookBaseNode->GetTextNodeById(69);
        AtkTextNode* region = spellbookBaseNode->GetTextNodeById(75);
        AtkImageNode* regionImage = spellbookBaseNode->GetImageNodeById(76);
        var spellNumberString = spellNumberTextnode->NodeText.ToString().Length >= 1
            ? spellNumberTextnode->NodeText.ToString()[1..] // Remove the # from the spell number
            : "";

        // Try to preserve last seen org text
        if (spellNumberString != lastSeenSpell)
            lastOrgText = emptyTextnode->NodeText.ToString();
        lastSeenSpell = spellNumberString;

        var success = Int32.TryParse(spellNumberString, out var spellNumber);
        if (!success) return;
        var spellSource = GetHintText(spellNumber);
        emptyTextnode->SetText($"{(lastOrgText != "" ? $"{lastOrgText}\n" : "")}{spellSource.Info}");
        emptyTextnode->AtkResNode.ToggleVisibility(true);

        // Change region if needed
        if (spellSource.Type != RegionType.Unknown)
            spellSource.SetRegion(region, regionImage);
    }

    private static SpellSource GetHintText(int spellNumber) =>
        Spells.TryGetValue(spellNumber, out var spell) ? spell.Source : new SpellSource($"Currently Unknown");

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        Services.CommandManager.RemoveHandler(CommandName);
        Services.Framework.Update -= AozNotebookAddonManager;
        Services.Framework.Update -= CheckLearnedSpells;

        NamePlateMarkerService.Dispose();

        Services.PluginInterface.UiBuilder.Draw -= DrawUi;
        Services.PluginInterface.UiBuilder.OpenMainUi -= DrawMainUi;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUi;
    }

    private void OnCommand(string command, string args) => DrawMainUi();
    private void DrawUi() => WindowSystem.Draw();
    private void DrawMainUi() => MainWindow.Toggle();
    private void DrawConfigUi() => ConfigWindow.Toggle();
    public void SetMapMarker(MapLinkPayload map) => Services.GameGui.OpenMapWithMapLink(map);

    private unsafe bool SpellUnlocked(uint unlockLink)
    {
        return UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(unlockLink);
    }

    public static void TeleportToNearestAetheryte(SpellSource location)
    {
        if (location.TerritoryType == null)
            return;

        Services.Log.Info($"ttid: {location.TerritoryTypeId}");

        var map = location.TerritoryType.Value.Map.Value;
        var nearestAetheryteId = MapMarkerSheet
            .SelectMany(x => x)
            .Where(x => x.DataType == 3 && x.RowId == map.MapMarkerRange)
            .Select(marker => new
            {
                distance = Vector2.DistanceSquared(
                    new Vector2(location.Location[0], location.Location[1]),
                    ConvertLocationToRaw(marker.X, marker.Y, map.SizeFactor)),
                rowId = marker.DataKey.RowId
            })
            .MinBy(x => x.distance);

        // Support the unique case of aetheryte not being in the same map
        var nearestAetheryte = nearestAetheryteId == null
            ? map.TerritoryType.Value.Aetheryte.Value
            : AetheryteSheet.FirstOrNull(x =>
                x.IsAetheryte && x.Territory.RowId == location.TerritoryTypeId && x.RowId == nearestAetheryteId.rowId);

        if (nearestAetheryte == null)
            return;

        TeleportConsumer.UseTeleport(nearestAetheryte.Value.RowId);
    }

    public static bool IsSpellUnlocked(int num) => !Configuration.ShowOnlyUnlearned ||
                                                   (UnlockedSpells.TryGetValue(num, out var isUnlocked) &&
                                                    !isUnlocked);


    private static Vector2 ConvertLocationToRaw(int x, int y, float scale)
    {
        var num = scale / 100f;
        return new Vector2(ConvertRawToMap((int)((x - 1024) * num * 1000f), scale),
            ConvertRawToMap((int)((y - 1024) * num * 1000f), scale));
    }

    private static float ConvertRawToMap(int pos, float scale)
    {
        var num1 = scale / 100f;
        var num2 = (float)(pos * (double)num1 / 1000.0f);
        return (40.96f / num1 * ((num2 + 1024.0f) / 2048.0f)) + 1.0f;
    }

    #region internal

    private void PrintTerris()
    {
        var mapSheet = Services.DataManager.GetExcelSheet<TerritoryType>();
        var contentSheet = Services.DataManager.GetExcelSheet<ContentFinderCondition>();
        foreach (var match in mapSheet)
        {
            if (match.RowId == 0) continue;
            if (match.Map.Value.PlaceName.Value.Name.ExtractText() == "") continue;
            Services.Log.Information("---------------");
            Services.Log.Information(match.Map.Value.PlaceName.Value.Name.ExtractText());
            Services.Log.Information($"TerriID: {match.RowId}");
            Services.Log.Information($"MapID: {match.Map.RowId}");

            var content = contentSheet.FirstOrNull(x => x.TerritoryType.RowId == match.RowId);
            if (content == null)
                continue;

            if (Utils.ToTitleCaseExtended(content.Value.Name) == "")
                continue;

            Services.Log.Information($"Duty: {Utils.ToTitleCaseExtended(content.Value.Name)}");
        }
    }

    private void PrintBlueSkills()
    {
        // skip the first non existing skill
        var aozActionTransients = Services.DataManager.GetExcelSheet<AozActionTransient>().Skip(1);
        var aozActions = Services.DataManager.GetExcelSheet<AozAction>().Skip(1);

        Dictionary<string, Spell> spells = new();
        foreach (var (transient, action) in aozActionTransients.Zip(aozActions))
        {
            spells.Add(transient.Number.ToString(),
                new Spell()
                {
                    Name = action.Action.Value!.Name.ToString(), Icon = transient.Icon,
                    Sources = { new SpellSource("", RegionType.Unknown) }
                });
        }

        Services.Log.Information(JsonConvert.SerializeObject(spells, Formatting.Indented));
    }

    #endregion
}