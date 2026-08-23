using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;

namespace Capy.NoRules.Features;

/// <summary>
/// Система отображения хитмаркеров и урона при стрельбе через единый HUD.
/// </summary>
public sealed class HitmarkerFeature
{
    private readonly HitmarkerConfig _config;

    public HitmarkerFeature(HitmarkerConfig config)
    {
        _config = config;
    }

    public void OnPlayerHurting(HurtingEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Attacker == null || ev.Player == null || ev.Attacker == ev.Player)
            return;

        if (_config.ShowDamageNumber && ev.Amount > 1f)
        {
            bool isKill = ev.Player.Health <= ev.Amount;
            ev.Attacker.ShowHitmarker(ev.Amount, isKill);
        }
    }
}
