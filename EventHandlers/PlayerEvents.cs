using Capy.NoRules.Features;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Scp096;
using Exiled.Events.EventArgs.Scp173;

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
    private readonly VanishFeature _vanish;

    public PlayerEvents(
        HitmarkerFeature hitmarkers,
        DotResKillFeature dotResKill,
        BetterCoinsFeature betterCoins,
        BetterEscapeFeature betterEscape,
        InfinityStuffFeature infinityStuff,
        VanishFeature vanish)
    {
        _hitmarkers = hitmarkers;
        _dotResKill = dotResKill;
        _betterCoins = betterCoins;
        _betterEscape = betterEscape;
        _infinityStuff = infinityStuff;
        _vanish = vanish;
    }

    public void OnHurting(HurtingEventArgs ev)
    {
        _vanish.OnHurting(ev);
        if (!ev.IsAllowed) return;

        _hitmarkers.OnPlayerHurting(ev);
    }

    public void OnDied(DiedEventArgs ev)
    {
        _hitmarkers.OnPlayerDied(ev);
        _dotResKill.OnPlayerDeath(ev);
    }

    public void OnSpawned(SpawnedEventArgs ev) => _betterCoins.OnPlayerSpawned(ev);
    public void OnFlippingCoin(FlippingCoinEventArgs ev) => _betterCoins.OnFlippingCoin(ev);
    public void OnEscaping(EscapingEventArgs ev) => _betterEscape.OnPlayerEscaping(ev);
    public void OnUsingRadioBattery(UsingRadioBatteryEventArgs ev) => _infinityStuff.OnUsingRadioBattery(ev);
    public void OnReloadingWeapon(ReloadingWeaponEventArgs ev) => _infinityStuff.OnReloadingWeapon(ev);
    public void OnLeft(LeftEventArgs ev) => _vanish.OnPlayerLeft(ev);

    public void OnDroppingAmmo(DroppingAmmoEventArgs ev)
    {
        _vanish.OnDroppingAmmo(ev);
        if (!ev.IsAllowed) return;

        _infinityStuff.OnDroppingAmmo(ev);
    }

    public void OnPickingUpItem(PickingUpItemEventArgs ev)
    {
        _vanish.OnPickingUpItem(ev);
        if (!ev.IsAllowed) return;

        _infinityStuff.OnPickingUpItem(ev);
    }

    public void OnDroppingItem(DroppingItemEventArgs ev) => _vanish.OnDroppingItem(ev);
    public void OnShooting(ShootingEventArgs ev) => _vanish.OnShooting(ev);
    public void OnInteractingDoor(InteractingDoorEventArgs ev) => _vanish.OnInteractingDoor(ev);
    public void OnInteractingLocker(InteractingLockerEventArgs ev) => _vanish.OnInteractingLocker(ev);
    public void OnInteractingElevator(InteractingElevatorEventArgs ev) => _vanish.OnInteractingElevator(ev);
    public void OnOpeningGenerator(OpeningGeneratorEventArgs ev) => _vanish.OnOpeningGenerator(ev);
    public void OnUnlockingGenerator(UnlockingGeneratorEventArgs ev) => _vanish.OnUnlockingGenerator(ev);
    public void OnActivatingGenerator(ActivatingGeneratorEventArgs ev) => _vanish.OnActivatingGenerator(ev);
    public void OnStoppingGenerator(StoppingGeneratorEventArgs ev) => _vanish.OnStoppingGenerator(ev);

    public void OnScp173AddingObserver(AddingObserverEventArgs ev) => _vanish.OnScp173AddingObserver(ev);
    public void OnScp096AddingTarget(AddingTargetEventArgs ev) => _vanish.OnScp096AddingTarget(ev);
}
