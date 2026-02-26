using System;
using System.Collections.Generic;
using Dalamud.Game.Gui.NamePlate;

namespace BlueMageHelper;

public static class NamePlateMarkerService
{
    //square blu mage icon - 61836
    //glowing blu mage icon - 62416
    //blue FC log, blu mage icon - 94129
    public static void Init()
    {
        Services.NamePlateGui.OnDataUpdate += NamePlateGuiOnOnDataUpdate;
    }

    private static void NamePlateGuiOnOnDataUpdate(INamePlateUpdateContext context,
        IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        var localPlayer = Services.ObjectTable.LocalPlayer;
        if (localPlayer == null) return;
        try
        {
            if (!Plugin.MobsByTerritory.TryGetValue(Services.ClientState.TerritoryType, out var record)) return;

            foreach (var h in handlers)
            {
                if (h.BattleChara == null || h.NamePlateKind != NamePlateKind.BattleNpcEnemy) continue;
                if (!record.NpcIds.Contains(h.BattleChara.BaseId)) continue;

                // Services.Log.Info($"{h.BattleChara.Name} {h.BattleChara.BaseId} {h.MarkerIconId}");
                if (h.MarkerIconId == 0)
                    h.MarkerIconId = 94129;
            }
        }
        catch (Exception e)
        {
            Services.Log.Error($"NamePlate not found {e}\n{e.StackTrace}");
        }
    }

    public static void Dispose()
    {
        Services.NamePlateGui.OnDataUpdate -= NamePlateGuiOnOnDataUpdate;
    }
}