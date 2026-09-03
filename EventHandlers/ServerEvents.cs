using Capy.NoRules.Addons;
using Capy.NoRules.Modules;
using Capy.NoRules.Concepts;
using Capy.NoRules.Scps;
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
    private readonly LobbyFeature _lobby;
    private readonly LobbyMusicFeature _lobbyMusic;

    public ServerEvents(DotResKillFeature dotResKill, FriendlyFireFeature friendlyFire, IntercomListFeature intercomList, Scp120Feature scp120, LobbyFeature lobby, LobbyMusicFeature lobbyMusic)
    {
        _dotResKill = dotResKill;
        _friendlyFire = friendlyFire;
        _intercomList = intercomList;
        _scp120 = scp120;
        _lobby = lobby;
        _lobbyMusic = lobbyMusic;
    }

    public void OnRoundStarted()
    {
        _lobbyMusic.OnRoundStarted();
        _lobby.OnRoundStarted();
        _dotResKill.OnRoundStarted();
        _friendlyFire.OnRoundStarted();
        _intercomList.OnRoundStarted();
        _scp120.OnRoundStarted();
    }

    public void OnRoundEnded(RoundEndedEventArgs ev)
    {
        _lobbyMusic.OnRoundEnded();
        _lobby.OnRoundEnded();
        _friendlyFire.OnRoundEnded();
        _intercomList.OnRoundEnded();
    }

    public void OnWaitingForPlayers()
    {
        _lobby.OnWaitingForPlayers();
        _lobbyMusic.OnWaitingForPlayers();
        _dotResKill.OnWaitingForPlayers();
        _intercomList.OnWaitingForPlayers();
    }

    public void OnRestartingRound()
    {
        _lobbyMusic.OnRestartingRound();
        _lobby.OnRestartingRound();
    }
}

