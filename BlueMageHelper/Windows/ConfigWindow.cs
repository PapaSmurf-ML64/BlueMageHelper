using System;
using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace BlueMageHelper.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin Plugin;

    public ConfigWindow(Plugin plugin) : base("Configuration##BlueMageHelper")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 460),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        using var tabBar = ImRaii.TabBar("##ConfigTabBar");
        if (!tabBar.Success)
            return;

        General();

        About();
    }

    private void General()
    {
        using var tabItem = ImRaii.TabItem("General");
        if (!tabItem.Success)
            return;

        var changed = false;

        ImGui.TextColored(ImGuiColors.DalamudViolet, "Vanilla Spellbook:");
        using (ImRaii.PushIndent(10.0f))
            changed |= ImGui.Checkbox("Show hint even if a spell is already unlocked.",
                ref Plugin.Configuration.ShowHintEvenIfUnlocked);

        ImGuiHelpers.ScaledDummy(5.0f);
        ImGui.TextColored(ImGuiColors.DalamudViolet, "Spellbook:");
        using (ImRaii.PushIndent(10.0f))
            changed |= ImGui.Checkbox("Show only unlearned spells.", ref Plugin.Configuration.ShowOnlyUnlearned);

        ImGuiHelpers.ScaledDummy(5.0f);
        ImGui.TextColored(ImGuiColors.DalamudViolet, "Mob Head Markers:");
        using (ImRaii.PushIndent(10.0f))
            changed |= ImGui.Checkbox("Mark mobs that drop a Blue spell.",
                ref Plugin.Configuration.MarkMobsInWorld);

        if (changed)
        {
            Services.Log.Info("Configuration changed, resetting.");
            Plugin.MainWindow.AllBlueSpells = null;
            Plugin.MainWindow.SelectedIndex = 0;
            Plugin.MainWindow.SelectedSource = 0;
            Plugin.Configuration.Save();
        }
    }

    private static void About()
    {
        using var tabItem = ImRaii.TabItem("About");
        if (!tabItem.Success)
            return;

        var buttonHeight = Helper.CalculateChildHeight();
        using (var contentChild = ImRaii.Child("Content", new Vector2(0, -buttonHeight)))
        {
            if (contentChild)
            {
                ImGuiHelpers.ScaledDummy(5.0f);

                ImGui.TextUnformatted("Author:");
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.ParsedGold, Services.PluginInterface.Manifest.Author);

                ImGui.TextUnformatted("Discord:");
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.ParsedGold, "@harbingerftw");

                ImGui.TextUnformatted("Version:");
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.ParsedOrange,
                    Services.PluginInterface.Manifest.AssemblyVersion.ToString());
            }
        }

        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(Helper.SeparatorPadding);

        using (var bottomChild = ImRaii.Child("Bottom", Vector2.Zero))
        {
            if (bottomChild.Success)
            {
                using (ImRaii.PushColor(ImGuiCol.Button, ImGuiColors.ParsedBlue))
                {
                    if (ImGui.Button("Discord Thread"))
                        Dalamud.Utility.Util.OpenLink(
                            "https://discord.com/channels/581875019861328007/1067487937735970846");
                }

                ImGui.SameLine();

                using (ImRaii.PushColor(ImGuiCol.Button, ImGuiColors.DPSRed))
                {
                    if (ImGui.Button("Issues"))
                        Dalamud.Utility.Util.OpenLink("https://github.com/harbingerftw/BlueMageHelper");
                }
            }
        }
    }
}