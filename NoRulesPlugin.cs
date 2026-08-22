using System;
using Exiled.API.Features;

namespace Capy.NoRules;

public sealed class NoRulesPlugin : Plugin<NoRulesConfig>
{
    public override string Name => "CapyLib.NoRules";
    public override string Author => "CapybaraPR";
    public override string Prefix => "capy-norules";
    public override Version Version => new(0, 1, 0);

    public static NoRulesPlugin Instance { get; private set; } = null!;

    public override void OnEnabled()
    {
        Instance = this;
        Log.Info("[NoRules] Каркас загружен: CustomItems / CustomRoles / Features / Additions.");
        base.OnEnabled();
    }

    public override void OnDisabled()
    {
        Instance = null!;
        base.OnDisabled();
    }
}
