using System;
using Capy.Core.Services;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using MEC;

namespace Capy.NoRules.Features;

/// <summary>
/// Система серверных оповещений и приветствий в стиле проекта Капибара.
/// </summary>
public sealed class BroadcastsFeature
{
    private readonly BroadcastsConfig _config;

    public BroadcastsFeature(BroadcastsConfig config)
    {
        _config = config;
    }

    public void OnPlayerVerified(VerifiedEventArgs ev)
    {
        if (ev.Player == null || string.IsNullOrWhiteSpace(_config.WelcomeMessage))
            return;

        Timing.CallDelayed(2.0f, () =>
        {
            if (ev.Player != null && ev.Player.IsConnected)
            {
                string msg = _config.WelcomeMessage
                    .Replace("%player_name%", ev.Player.Nickname)
                    .Replace("%server_tps%", Server.Tps.ToString("F0"));

                ev.Player.Broadcast(_config.WelcomeDuration, msg);
            }
        });
    }

    public void OnRoundStarted()
    {
        if (string.IsNullOrWhiteSpace(_config.RoundStartMessage))
            return;

        Map.Broadcast(_config.RoundStartDuration, _config.RoundStartMessage);
    }

    public void OnRoundEnded(RoundEndedEventArgs ev)
    {
        if (!_config.ShowRoundEndSummary)
            return;

        string summary = $"<color=#ffa94e><b>РАУНД ЗАВЕРШЁН!</b></color>\n<color=#c2c2c2>Победившая сторона: <color=#ffd285>{ev.LeadingTeam}</color> • Время раунда: <color=#ffd285>{Round.ElapsedTime.Minutes} мин {Round.ElapsedTime.Seconds} сек</color></color>";
        Map.Broadcast(8, summary);
    }
}
