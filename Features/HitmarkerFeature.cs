using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;

namespace Capy.NoRules.Features;

/// <summary>
/// Система отображения хитмаркеров и урона при стрельбе.
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
            string hpLeft = ev.Player.Health > ev.Amount 
                ? $"<color=#ff4444>-{(int)ev.Amount} HP</color> <color=#888888>({(int)(ev.Player.Health - ev.Amount)} HP)</color>" 
                : "<color=#ff0000><b>УБИТ 💀</b></color>";

            ev.Attacker.ShowHint($"<align=center><size=20>{hpLeft}</size></align>", 1.0f);
        }
    }
}
