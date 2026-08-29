using System;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Server;

namespace Capy.NoRules.Addons;

/// <summary>
/// Уведомления о репортах: админам с правом AdminChat показывается хинт
/// «[Репорт на X] Проверьте Staff Chat», репортёр получает подтверждение.
/// </summary>
public sealed class ShowReportsFeature
{
    private readonly ShowReportsConfig _config;
    private bool _enabled;

    public ShowReportsFeature(ShowReportsConfig config)
    {
        _config = config;
    }

    public void Enable()
    {
        if (_enabled || !_config.IsEnabled) return;
        _enabled = true;

        Exiled.Events.Handlers.Server.ReportingCheater += OnReportingCheater;
        Exiled.Events.Handlers.Server.LocalReporting += OnLocalReporting;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        Exiled.Events.Handlers.Server.ReportingCheater -= OnReportingCheater;
        Exiled.Events.Handlers.Server.LocalReporting -= OnLocalReporting;
    }

    private void OnReportingCheater(ReportingCheaterEventArgs ev) => HandleReport(ev.Player, ev.Target);

    private void OnLocalReporting(LocalReportingEventArgs ev) => HandleReport(ev.Player, ev.Target);

    private void HandleReport(Player? reporter, Player? target)
    {
        if (!_config.IsEnabled || reporter == null || target == null)
            return;

        // Себя не репортим
        if (string.Equals(reporter.UserId, target.UserId, StringComparison.OrdinalIgnoreCase))
            return;

        string targetName = target.Nickname;

        foreach (Player admin in Player.List.Where(IsAdminChatPermitted))
        {
            admin.ShowZoneHint(
                HintZone.Notification,
                $"<b>[<color=#ffd285>Репорт</color> на <color=#f87171>{targetName}</color>]\n<size=24><color=#c2c2c2>Проверьте Staff Chat (M)</color></size></b>",
                _config.AdminNotifyDuration,
                "report",
                28);
        }

        reporter.Broadcast((ushort)System.Math.Min(60.0, System.Math.Max(1.0, _config.ReporterMessageDuration)),
            $"<b>{_config.ReporterMessage.Replace("{target}", targetName)}</b>");
    }

    private static bool IsAdminChatPermitted(Player player)
    {
        try
        {
            var group = player?.Group;
            return group != null && PermissionsHandler.IsPermitted(group.Permissions, PlayerPermissions.AdminChat);
        }
        catch
        {
            return false;
        }
    }
}

