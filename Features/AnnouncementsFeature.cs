using System;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using MEC;

namespace Capy.NoRules.Features;

/// <summary>
/// Система серверных оповещений и приветствий, работающая строго через экранный HUD (без Map.Broadcast).
/// </summary>
public sealed class AnnouncementsFeature
{
    private readonly AnnouncementsConfig _config;

    public AnnouncementsFeature(AnnouncementsConfig config)
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

                ev.Player.ShowAnnouncement(msg, _config.WelcomeDuration, "welcome_hud");
            }
        });
    }

    public void OnRoundStarted()
    {
        if (string.IsNullOrWhiteSpace(_config.RoundStartMessage))
            return;

        ShowHintExtensions.ShowAnnouncementToAll(_config.RoundStartMessage, _config.RoundStartDuration, "round_start_hud");
    }

    public void OnRoundEnded(RoundEndedEventArgs ev)
    {
        if (!_config.ShowRoundEndSummary)
            return;

        string summary = $"<color=#ffa94e><b>РАУНД ЗАВЕРШЁН!</b></color>\n<color=#c2c2c2>Победившая сторона: <color=#ffd285>{ev.LeadingTeam}</color> • Время: <color=#ffd285>{Round.ElapsedTime.Minutes}м {Round.ElapsedTime.Seconds}с</color></color>";
        ShowHintExtensions.ShowAnnouncementToAll(summary, 7.0f, "round_end_hud");
    }
}
