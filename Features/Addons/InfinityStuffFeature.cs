using System.Linq;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Pickups;
using Exiled.Events.EventArgs.Map;
using Exiled.Events.EventArgs.Player;
using MEC;

namespace Capy.NoRules.Features;

/// <summary>
/// Система бесконечных ресурсов и полного исчезновения патронов из мира:
/// 1. Бесконечный заряд рации.
/// 2. Авто-пополнение магазина при перезарядке.
/// 3. Мгновенное удаление любых выброшенных или заспавненных патронов с карты (как в AspectLib).
/// 4. Запрет ручного выбрасывания и подбора патронов.
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

        int needed = ev.Firearm.MaxMagazineAmmo - ev.Firearm.MagazineAmmo;
        if (needed > 0)
        {
            ev.Player.AddAmmo(ev.Firearm.AmmoType, (ushort)needed);
        }
    }

    public void OnSpawned(SpawnedEventArgs ev)
    {
        if (!_config.InfiniteAmmo || ev.Player == null) return;

        Timing.CallDelayed(1.0f, () =>
        {
            if (ev.Player != null && ev.Player.IsConnected && ev.Player.IsAlive)
            {
                ev.Player.SetAmmo(AmmoType.Nato762, 1);
                ev.Player.SetAmmo(AmmoType.Nato556, 1);
                ev.Player.SetAmmo(AmmoType.Nato9, 1);
                ev.Player.SetAmmo(AmmoType.Ammo44Cal, 1);
                ev.Player.SetAmmo(AmmoType.Ammo12Gauge, 1);
            }
        });
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
        if (_config.RemoveAmmoDrops && ev.Pickup != null && ev.Pickup.Category == ItemCategory.Ammo)
        {
            ev.IsAllowed = false;
            ev.Pickup.Destroy();
        }
    }

    public void OnSearchingPickup(SearchingPickupEventArgs ev)
    {
        if (_config.RemoveAmmoDrops && ev.Pickup != null && ev.Pickup.Category == ItemCategory.Ammo)
        {
            ev.IsAllowed = false;
            ev.Pickup.Destroy();
        }
    }

    public void OnHandcuffing(HandcuffingEventArgs ev)
    {
        if (_config.InfiniteAmmo && ev.Target != null)
        {
            ev.Target.ClearAmmo();
        }
    }

    public void OnPickupAdded(PickupAddedEventArgs ev)
    {
        if (_config.RemoveAmmoDrops && ev.Pickup != null && ev.Pickup.Category == ItemCategory.Ammo)
        {
            ev.Pickup.Destroy();
        }
    }

    public void OnRoundStarted()
    {
        if (!_config.RemoveAmmoDrops) return;

        Timing.CallDelayed(2.0f, () =>
        {
            foreach (var pickup in Pickup.List.Where(p => p != null && p.Category == ItemCategory.Ammo).ToList())
            {
                try { pickup.Destroy(); } catch { }
            }
        });
    }
}
