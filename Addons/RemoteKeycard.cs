using System.Linq;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Items;
using Exiled.Events.EventArgs.Player;

namespace Capy.NoRules.Addons;

/// <summary>
/// Remote Keycard: карты доступа работают из любого слота инвентаря, а не только из рук.
/// Двери и шкафчики (конфигурируется).
/// </summary>
public sealed class RemoteKeycardFeature
{
    private readonly RemoteKeycardConfig _config;
    private bool _enabled;

    public RemoteKeycardFeature(RemoteKeycardConfig config)
    {
        _config = config;
    }

    public void Enable()
    {
        if (_enabled || !_config.IsEnabled) return;
        _enabled = true;

        if (_config.AllowDoors)
            Exiled.Events.Handlers.Player.InteractingDoor += OnInteractingDoor;

        if (_config.AllowLockers)
            Exiled.Events.Handlers.Player.InteractingLocker += OnInteractingLocker;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        Exiled.Events.Handlers.Player.InteractingDoor -= OnInteractingDoor;
        Exiled.Events.Handlers.Player.InteractingLocker -= OnInteractingLocker;
    }

    private static bool IsPermitted(Player player, KeycardPermissions needed)
    {
        return player.Items.OfType<Keycard>().Any(k => (needed & k.Permissions) != 0);
    }

    private void OnInteractingDoor(InteractingDoorEventArgs ev)
    {
        if (ev.Player == null || ev.Player.IsBypassModeEnabled || ev.IsAllowed)
            return;

        if (ev.Door.IsLocked || ev.Door.KeycardPermissions == KeycardPermissions.None)
            return;

        if (!IsPermitted(ev.Player, ev.Door.KeycardPermissions))
            return;

        ev.IsAllowed = true;
        ev.CanInteract = true;

        if (_config.ShowHint)
            ev.Player.ShowHint("<color=#7dd3fc>🔑 Карта из инвентаря открыла дверь</color>", 1.5f);
    }

    private void OnInteractingLocker(InteractingLockerEventArgs ev)
    {
        if (ev.Player == null || ev.Player.IsBypassModeEnabled || ev.IsAllowed)
            return;

        var chamber = ev.InteractingChamber;
        if (chamber == null || !chamber.CanInteract)
            return;

        if (!IsPermitted(ev.Player, chamber.RequiredPermissions))
            return;

        ev.IsAllowed = true;

        if (_config.ShowHint)
            ev.Player.ShowHint("<color=#7dd3fc>🔑 Карта из инвентаря открыла шкафчик</color>", 1.5f);
    }
}

