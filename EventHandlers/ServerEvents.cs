using Capy.NoRules.Features;
using Exiled.Events.EventArgs.Server;

namespace Capy.NoRules.EventHandlers;

/// <summary>
/// Обработчик событий сервера и раунда для сервера NoRules.
/// </summary>
public sealed class ServerEvents
{
    private readonly BroadcastsFeature _broadcasts;

    public ServerEvents(BroadcastsFeature broadcasts)
    {
        _broadcasts = broadcasts;
    }

    public void OnRoundStarted() => _broadcasts.OnRoundStarted();
    public void OnRoundEnded(RoundEndedEventArgs ev) => _broadcasts.OnRoundEnded(ev);
}
