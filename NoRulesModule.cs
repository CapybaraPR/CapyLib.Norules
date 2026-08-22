using System;
using System.Threading.Tasks;
using Capy.Core.API;

namespace Capy.NoRules;

public class NoRulesModule : ICapyModule
{
    public string Name => "NoRules";
    public string Author => "CapybaraPR";

    public Version Version => new(0, 1, 0);

    public bool IsEnabled { get; set; }

    public ICapyLogger Log { get; set; } = null!;

    private NoRulesConfig _config = new();

    public void OnEnabled() { }
    public void OnDisabled() { }

    public Task OnEnabledAsync()
    {
        Log.Info($"[NoRules] Модуль включён (v{Version}, debug: {_config.DebugMode}). Папки: CustomItems / CustomRoles / Features / Additions.");
        return Task.CompletedTask;
    }

    public Task OnDisabledAsync()
    {
        Log.Info("[NoRules] Модуль выключен.");
        return Task.CompletedTask;
    }

    public Type GetConfigType() => typeof(NoRulesConfig);

    public void SetConfig(object config) =>
        _config = config as NoRulesConfig ?? new NoRulesConfig();
}

