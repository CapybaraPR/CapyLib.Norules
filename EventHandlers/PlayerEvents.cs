using Capy.NoRules.Features;
using Exiled.Events.EventArgs.Player;

namespace Capy.NoRules.EventHandlers;

/// <summary>
/// Маршрутизатор событий игроков для сервера NoRules.
/// </summary>
public sealed class PlayerEvents
{
    private readonly HitmarkerFeature _hitmarkers;
    private readonly DotResKillFeature _dotResKill;
    private readonly BetterCoinsFeature _betterCoins;
    private readonly BetterEscapeFeature _betterEscape;
    private readonly InfinityStuffFeature _infinityStuff;

    public PlayerEvents(
        HitmarkerFeature hitmarkers,
        DotResKillFeature dotResKill,
        BetterCoinsFeature betterCoins,
        BetterEscapeFeature betterEscape,
        InfinityStuffFeature infinityStuff)
    {
        _hitmarkers = hitmarkers;
        _dotResKill = dotResKill;
        _betterCoins = betterCoins;
        _betterEscape = betterEscape;
        _infinityStuff = infinityStuff;
    }

    public void OnHurting(HurtingEventArgs ev) => _hitmarkers.OnPlayerHurting(ev);
    public void OnDied(DiedEventArgs ev) => _dotResKill.OnPlayerDeath(ev);
    public void OnSpawned(SpawnedEventArgs ev) => _betterCoins.OnPlayerSpawned(ev);
    public void OnFlippingCoin(FlippingCoinEventArgs ev) => _betterCoins.OnFlippingCoin(ev);
    public void OnEscaping(EscapingEventArgs ev) => _betterEscape.OnPlayerEscaping(ev);
    public void OnUsingRadioBattery(UsingRadioBatteryEventArgs ev) => _infinityStuff.OnUsingRadioBattery(ev);
    public void OnReloadingWeapon(ReloadingWeaponEventArgs ev) => _infinityStuff.OnReloadingWeapon(ev);
    public void OnDroppingAmmo(DroppingAmmoEventArgs ev) => _infinityStuff.OnDroppingAmmo(ev);
    public void OnPickingUpItem(PickingUpItemEventArgs ev) => _infinityStuff.OnPickingUpItem(ev);
}
