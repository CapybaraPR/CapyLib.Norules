using Capy.NoRules.Features;
using Exiled.Events.EventArgs.Player;

namespace Capy.NoRules.EventHandlers;

/// <summary>
/// Обработчик событий игроков для сервера NoRules.
/// </summary>
public sealed class PlayerEvents
{
    private readonly HitmarkerFeature _hitmarkers;
    private readonly AnnouncementsFeature _announcements;

    public PlayerEvents(HitmarkerFeature hitmarkers, AnnouncementsFeature announcements)
    {
        _hitmarkers = hitmarkers;
        _announcements = announcements;
    }

    public void OnHurting(HurtingEventArgs ev) => _hitmarkers.OnPlayerHurting(ev);
    public void OnVerified(VerifiedEventArgs ev) => _announcements.OnPlayerVerified(ev);
}
