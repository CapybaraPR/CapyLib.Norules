using Capy.NoRules.Config;
using Exiled.Events.EventArgs.Player;

namespace Capy.NoRules.Features;

/// <summary>
/// Улучшенная система связывания игроков для динамичного режима NoRules.
/// </summary>
public sealed class FastDisarmFeature
{
    private readonly FastDisarmConfig _config;

    public FastDisarmFeature(FastDisarmConfig config)
    {
        _config = config;
    }

    public void OnHandcuffing(HandcuffingEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Player == null || ev.Target == null || !ev.IsAllowed)
            return;

        // Если включено мгновенное связывание
        if (_config.InstantDisarm)
        {
            ev.Target.Handcuff();
            ev.IsAllowed = false;
        }
    }
}
