using System.Linq;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using InventorySystem.Items;

namespace Capy.NoRules.Addons;

/// <summary>
/// Facility Auth: тесла-ворота не активируются игроком, у которого есть любая карта доступа,
/// кроме KeycardChaosInsurgency (карта Хаоса, наоборот, делает целью).
/// </summary>
public sealed class FacilityAuthFeature
{
    private readonly FacilityAuthConfig _config;
    private bool _enabled;

    public FacilityAuthFeature(FacilityAuthConfig config)
    {
        _config = config;
    }

    public void Enable()
    {
        if (_enabled || !_config.IsEnabled) return;
        _enabled = true;

        Exiled.Events.Handlers.Player.TriggeringTesla += OnTriggeringTesla;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        Exiled.Events.Handlers.Player.TriggeringTesla -= OnTriggeringTesla;
    }

    private void OnTriggeringTesla(TriggeringTeslaEventArgs ev)
    {
        if (ev.Player == null || !ev.Player.IsAlive)
            return;

        // Как в оригинале Hazbin: есть любая keycard кроме карты Хаоса — тесла блокируется
        if (ev.Player.Items.Any(i => i is { Category: ItemCategory.Keycard, Type: not ItemType.KeycardChaosInsurgency }))
            ev.IsAllowed = false;
    }
}

