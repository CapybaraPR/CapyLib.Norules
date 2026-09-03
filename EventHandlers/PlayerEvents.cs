using Capy.NoRules.Addons;
using Capy.NoRules.Modules;
using Capy.NoRules.Concepts;
using Capy.NoRules.Scps;
using Exiled.Events.EventArgs.Player;

namespace Capy.NoRules.EventHandlers;

public class PlayerEvents
{
    private readonly HitmarkerFeature _hitmarkers;
    private readonly DotResKillFeature _dotResKill;
    private readonly BetterCoinsFeature _betterCoins;
    private readonly BetterEscapeFeature _betterEscape;
    private readonly InfinityStuffFeature _infinityStuff;
    private readonly VanishFeature _vanish;
    private readonly CapybaraPetFeature _capybaraPet;
    private readonly Scp120Feature _scp120;
    private readonly LobbyFeature _lobby;

    public PlayerEvents(
        HitmarkerFeature hitmarkers,
        DotResKillFeature dotResKill,
        BetterCoinsFeature betterCoins,
        BetterEscapeFeature betterEscape,
        InfinityStuffFeature infinityStuff,
        VanishFeature vanish,
        CapybaraPetFeature capybaraPet,
        Scp120Feature scp120,
        LobbyFeature lobby)
    {
        _hitmarkers = hitmarkers;
        _dotResKill = dotResKill;
        _betterCoins = betterCoins;
        _betterEscape = betterEscape;
        _infinityStuff = infinityStuff;
        _vanish = vanish;
        _capybaraPet = capybaraPet;
        _scp120 = scp120;
        _lobby = lobby;
    }

    public void OnHurting(HurtingEventArgs ev)
    {
        _lobby.OnPlayerHurting(ev);
        if (!ev.IsAllowed) return;
        _hitmarkers.OnPlayerHurting(ev);
    }
    public void OnDied(DiedEventArgs ev)
    {
        _hitmarkers.OnPlayerDied(ev);
        _dotResKill.OnPlayerDeath(ev);
        _capybaraPet.OnPlayerDeath(ev);
    }

    public void OnSpawned(SpawnedEventArgs ev)
    {
        _lobby.OnPlayerSpawned(ev);
        _betterCoins.OnPlayerSpawned(ev);
        _infinityStuff.OnSpawned(ev);
        _capybaraPet.OnPlayerSpawned(ev);
    }

    public void OnVerified(VerifiedEventArgs ev)
    {
        _lobby.OnPlayerVerified(ev);
    }

    public void OnFlippingCoin(FlippingCoinEventArgs ev)
    {
        _vanish.OnFlippingCoin(ev);
        _betterCoins.OnFlippingCoin(ev);
    }

    public void OnEscaping(EscapingEventArgs ev) => _betterEscape.OnPlayerEscaping(ev);
    public void OnUsingRadioBattery(UsingRadioBatteryEventArgs ev) => _infinityStuff.OnUsingRadioBattery(ev);
    public void OnReloadingWeapon(ReloadingWeaponEventArgs ev) => _infinityStuff.OnReloadingWeapon(ev);
    public void OnSearchingPickup(SearchingPickupEventArgs ev) => _infinityStuff.OnSearchingPickup(ev);
    public void OnHandcuffing(HandcuffingEventArgs ev) => _infinityStuff.OnHandcuffing(ev);
    public void OnLeft(LeftEventArgs ev)
    {
        _vanish.OnPlayerLeft(ev);
        _capybaraPet.OnPlayerLeft(ev);
    }

    public void OnDroppingAmmo(DroppingAmmoEventArgs ev)
    {
        if (VanishFeature.IsVanished(ev.Player))
        {
            ev.IsAllowed = false;
            return;
        }
        _infinityStuff.OnDroppingAmmo(ev);
    }

    public void OnPickingUpItem(PickingUpItemEventArgs ev)
    {
        if (VanishFeature.IsVanished(ev.Player))
        {
            ev.IsAllowed = false;
            return;
        }

        _infinityStuff.OnPickingUpItem(ev);
        _scp120.OnPickingUpItem(ev);
    }
}

