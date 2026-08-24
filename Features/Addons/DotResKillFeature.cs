using System;
using System.Collections.Generic;
using Capy.Core.Features;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Features;

/// <summary>
/// Система команд .kill (самоубийство) и .res (возрождение в первые минуты раунда).
/// </summary>
public sealed class DotResKillFeature
{
    private readonly DotResKillConfig _config;
    public bool ResAllowed { get; set; } = true;
    public bool KillAllowed { get; set; } = true;

    private readonly Dictionary<int, DateTime> _resCooldowns = new();
    private readonly Dictionary<int, DateTime> _killCooldowns = new();

    public DotResKillFeature(DotResKillConfig config)
    {
        _config = config;
    }

    public void OnPlayerDeath(DiedEventArgs ev)
    {
        if (ev.Player == null || !_config.ResEnabled || !ResAllowed)
            return;

        // Если раунд только начался (в пределах ResWindowSeconds)
        if (Round.ElapsedTime.TotalSeconds <= _config.ResWindowSeconds)
        {
            ev.Player.ShowAnnouncement(
                "<color=#ffa94e>Вы можете возродиться!</color>\n<color=#c2c2c2>Напишите <color=#ffd285>.res</color> в консоли [~]</color>",
                5.0f,
                "res_hint"
            );
        }
    }

    public void OnRoundStarted()
    {
        ResAllowed = _config.ResEnabled;
        KillAllowed = _config.KillEnabled;
        _resCooldowns.Clear();
        _killCooldowns.Clear();
    }

    public void OnWaitingForPlayers()
    {
        ResAllowed = false;
        KillAllowed = false;
        _resCooldowns.Clear();
        _killCooldowns.Clear();
    }

    public bool ExecuteKill(Player player, out string response)
    {
        if (!_config.KillEnabled || !KillAllowed)
        {
            response = "Команда .kill отключена на сервере.";
            return false;
        }

        if (!player.IsAlive || player.Role.Type == RoleTypeId.Spectator)
        {
            response = "Вы должны быть живы, чтобы использовать .kill.";
            return false;
        }

        if (_killCooldowns.TryGetValue(player.Id, out var expire) && DateTime.UtcNow < expire)
        {
            double remaining = (expire - DateTime.UtcNow).TotalSeconds;
            response = $"Подождите ещё {remaining:F1} сек. перед следующим вызовом .kill.";
            return false;
        }

        string reason = _config.KillReasons.Count > 0
            ? _config.KillReasons[UnityEngine.Random.Range(0, _config.KillReasons.Count)]
            : "Суицид";

        player.Kill(reason);
        _killCooldowns[player.Id] = DateTime.UtcNow.AddSeconds(_config.KillCooldownSeconds);

        response = $"<color=#ff4444>{reason}</color>";
        return true;
    }

    public bool ExecuteRes(Player player, out string response)
    {
        if (!_config.ResEnabled || !ResAllowed)
        {
            response = "Команда .res отключена на сервере.";
            return false;
        }

        if (Round.ElapsedTime.TotalSeconds > _config.ResWindowSeconds)
        {
            response = $"Время для возрождения истекло (прошло больше {(int)_config.ResWindowSeconds} сек).";
            return false;
        }

        if (player.IsAlive && player.Role.Type != RoleTypeId.Spectator)
        {
            response = "Вы должны быть наблюдателем (Spectator), чтобы использовать .res.";
            return false;
        }

        if (_resCooldowns.TryGetValue(player.Id, out var expire) && DateTime.UtcNow < expire)
        {
            double remaining = (expire - DateTime.UtcNow).TotalSeconds;
            response = $"Подождите ещё {remaining:F1} сек. перед повторной попыткой.";
            return false;
        }

        RoleTypeId targetRole = UnityEngine.Random.Range(0f, 100f) <= _config.ResClassDChance
            ? RoleTypeId.ClassD
            : RoleTypeId.Scientist;

        player.Role.Set(targetRole);
        _resCooldowns[player.Id] = DateTime.UtcNow.AddSeconds(_config.ResCooldownSeconds);

        string roleName = targetRole == RoleTypeId.ClassD
            ? "<color=#ff9933>Класс D</color>"
            : "<color=#ffff33>Учёного</color>";

        player.ShowAnnouncement($"Вы успешно возродились за {roleName}!", 4.0f, "res_success");

        response = $"Вы успешно возродились за {roleName}!";
        return true;
    }
}
