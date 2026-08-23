using Capy.NoRules.Features;
using Exiled.Events.EventArgs.Server;

namespace Capy.NoRules.EventHandlers;

/// <summary>
/// Маршрутизатор событий сервера для режима NoRules.
/// </summary>
public sealed class ServerEvents
{
    private readonly DotResKillFeature _dotResKill;
    private readonly FriendlyFireFeature _friendlyFire;
    private readonly IntercomListFeature _intercomList;
    private readonly Scp120Feature _scp120;

    public ServerEvents(DotResKillFeature dotResKill, FriendlyFireFeature friendlyFire, IntercomListFeature intercomList, Scp120Feature scp120)
    {
        _dotResKill = dotResKill;
        _friendlyFire = friendlyFire;
        _intercomList = intercomList;
        _scp120 = scp120;
    }

    public void OnRoundStarted()
    {
        _dotResKill.OnRoundStarted();
        _friendlyFire.OnRoundStarted();
        _intercomList.OnRoundStarted();
        _scp120.OnRoundStarted();
    }

    public void OnRoundEnded(RoundEndedEventArgs ev)
    {
        _friendlyFire.OnRoundEnded();
        _intercomList.OnRoundEnded();
    }

    public void OnWaitingForPlayers()
    {
        _dotResKill.OnWaitingForPlayers();
        _intercomList.OnWaitingForPlayers();
    }
}
