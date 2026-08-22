using System;
using System.Threading.Tasks;
using Capy.Core.API;

namespace Capy.NoRules;

/// <summary>
/// Единый плагин NoRules: весь контент (CustomItems / CustomRoles / Features / Additions) живёт здесь.
/// </summary>
public sealed class NoRulesPlugin : GamemodePlugin<NoRulesConfig>
{
    public override string Name => "NoRules";
    public override Version Version => new(0, 1, 0);

    public override int[] Ports { get; } = { 7777 };

    public static NoRulesPlugin Instance { get; private set; } = null!;

    public override Task OnEnabledAsync()
    {
        Instance = this;
        Log.Info($"[NoRules] Плагин включён (v{Version}, debug: {Config.DebugMode}). CustomItems / CustomRoles / Features / Additions.");
        return Task.CompletedTask;
    }

    public override Task OnDisabledAsync()
    {
        Log.Info("[NoRules] Плагин выключен.");
        Instance = null!;
        return Task.CompletedTask;
    }
}
