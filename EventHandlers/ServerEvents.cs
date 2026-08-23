using Capy.NoRules.Features;
using Exiled.Events.EventArgs.Server;

namespace Capy.NoRules.EventHandlers;

/// <summary>
/// Обработчик событий сервера и раунда для сервера NoRules.
/// </summary>
public sealed class ServerEvents
{
    private readonly AnnouncementsFeature _announcements;

    public ServerEvents(AnnouncementsFeature announcements)
    {
        _announcements = announcements;
    }

    public void OnRoundStarted() => _announcements.OnRoundStarted();
    public void OnRoundEnded(RoundEndedEventArgs ev) => _announcements.OnRoundEnded(ev);
}
