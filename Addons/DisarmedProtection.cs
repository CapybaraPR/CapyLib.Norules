using System;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;

namespace Capy.NoRules.Addons;

/// <summary>
/// Защита связанных и обезоруженных пленных от токсичного расстрела союзниками или противниками.
/// Стимулирует эвакуацию и конвой вместо бессмысленного DM.
/// </summary>
public sealed class DisarmedProtectionFeature
{
    private readonly DisarmedProtectionConfig _config;

    public DisarmedProtectionFeature(DisarmedProtectionConfig config)
    {
        _config = config;
    }

    public void OnPlayerHurting(HurtingEventArgs ev)
    {
        if (!_config.IsEnabled) return;

        if (ev.Player == null || ev.Attacker == null || ev.Player == ev.Attacker) return;

        // Проверяем, обезоружен (связан) ли игрок
        if (!ev.Player.IsCuffed) return;

        RoleTypeId victimRole = ev.Player.Role.Type;
        bool isProtectedRole = (victimRole == RoleTypeId.ClassD && _config.ProtectClassD) ||
                               (victimRole == RoleTypeId.Scientist && _config.ProtectScientists);

        if (!isProtectedRole) return;

        // Проверяем, является ли нападающий тем, кто лично связал пленного
        bool isCaptor = ev.Attacker == ev.Player.Cuffer;
        if (isCaptor && _config.AllowCaptorToKill)
        {
            return;
        }

        // Проверяем, нападает ли SCP
        bool isScpAttacker = ev.Attacker.Role.Team == Team.SCPs;
        if (isScpAttacker && !_config.ProtectFromScps)
        {
            return;
        }

        // Блокируем урон по связанному пленному
        ev.IsAllowed = false;
        ev.Amount = 0f;

        if (_config.NotifyAttacker)
        {
            try
            {
                ev.Attacker.ShowZoneHint(
                    HintZone.Notification,
                    _config.WarningMessage,
                    2.5f,
                    "disarmed_prot_warning",
                    25
                );
            }
            catch { }
        }
    }
}
