using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.Events.EventArgs.Player;

namespace Capy.NoRules.Features;

/// <summary>
/// Система бесконечных патронов, бесконечной рации и очистки ненужных патронов.
/// </summary>
public sealed class InfinityStuffFeature
{
    private readonly InfinityStuffConfig _config;

    public InfinityStuffFeature(InfinityStuffConfig config)
    {
        _config = config;
    }

    public void OnUsingRadioBattery(UsingRadioBatteryEventArgs ev)
    {
        if (_config.InfiniteRadio)
        {
            ev.Drain = 0f;
            ev.IsAllowed = true;
        }
    }

    public void OnReloadingWeapon(ReloadingWeaponEventArgs ev)
    {
        if (!_config.InfiniteAmmo || ev.Player == null || ev.Firearm == null)
            return;

        // Пополняем патроны в запас игрока до максимума магазина
        int neededAmmo = ev.Firearm.MaxMagazineAmmo - ev.Firearm.MagazineAmmo;
        if (neededAmmo > 0)
        {
            ev.Player.AddAmmo(ev.Firearm.AmmoType, (ushort)neededAmmo);
        }
    }

    public void OnDroppingAmmo(DroppingAmmoEventArgs ev)
    {
        if (_config.RemoveAmmoDrops)
        {
            ev.IsAllowed = false;
        }
    }

    public void OnPickingUpItem(PickingUpItemEventArgs ev)
    {
        if (_config.RemoveAmmoDrops && ev.Pickup != null && IsAmmoType(ev.Pickup.Type))
        {
            ev.IsAllowed = false;
            ev.Pickup.Destroy();
        }
    }

    private static bool IsAmmoType(ItemType type)
    {
        return type is ItemType.Ammo9x19 or ItemType.Ammo556x45 or ItemType.Ammo762x39 or ItemType.Ammo12gauge or ItemType.Ammo44cal;
    }
}
