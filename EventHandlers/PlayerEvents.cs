using Capy.NoRules.Features;
using Exiled.Events.EventArgs.Player;

namespace Capy.NoRules.EventHandlers;

/// <summary>
/// Обработчик событий игроков для сервера NoRules.
/// </summary>
public sealed class PlayerEvents
{
    private readonly AutoDoorsFeature _autoDoors;
    private readonly HitmarkerFeature _hitmarkers;
    private readonly FastDisarmFeature _fastDisarm;
    private readonly BroadcastsFeature _broadcasts;

    public PlayerEvents(AutoDoorsFeature autoDoors, HitmarkerFeature hitmarkers, FastDisarmFeature fastDisarm, BroadcastsFeature broadcasts)
    {
        _autoDoors = autoDoors;
        _hitmarkers = hitmarkers;
        _fastDisarm = fastDisarm;
        _broadcasts = broadcasts;
    }

    public void OnInteractingDoor(InteractingDoorEventArgs ev) => _autoDoors.OnInteractingDoor(ev);
    public void OnHurting(HurtingEventArgs ev) => _hitmarkers.OnPlayerHurting(ev);
    public void OnHandcuffing(HandcuffingEventArgs ev) => _fastDisarm.OnHandcuffing(ev);
    public void OnVerified(VerifiedEventArgs ev) => _broadcasts.OnPlayerVerified(ev);
}
