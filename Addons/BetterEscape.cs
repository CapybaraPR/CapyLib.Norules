using System.Linq;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.Events.EventArgs.Player;

namespace Capy.NoRules.Addons;

/// <summary>
/// Расширенная система сценариев побега (связанные D-классы, ученые, охрана).
/// </summary>
public sealed class BetterEscapeFeature
{
    private readonly BetterEscapeConfig _config;

    public BetterEscapeFeature(BetterEscapeConfig config)
    {
        _config = config;
    }

    public void OnPlayerEscaping(EscapingEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Player == null || _config.Scenarios == null || _config.Scenarios.Count == 0)
            return;

        var scenario = _config.Scenarios.FirstOrDefault(s => 
            s.OldRole == ev.Player.Role.Type && 
            s.IsCuffed == ev.Player.IsCuffed
        );

        if (scenario != null)
        {
            ev.NewRole = scenario.NewRole;
            ev.IsAllowed = true;
            ev.Player.ShowAnnouncement("<color=#a3e635><b>ВЫ УСПЕШНО СБЕЖАЛИ!</b></color>", 4.5f, "escape_hud");
        }
    }
}

