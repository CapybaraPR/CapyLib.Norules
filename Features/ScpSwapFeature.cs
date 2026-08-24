using System;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Core.Extensions;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using CommandSystem;
using Exiled.API.Extensions;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using PlayerRoles;

namespace Capy.NoRules.Features;

/// <summary>
/// SCP-swap: SCP-игрок может сменить роль командой .swap в первые N секунд раунда.
/// Свап только на свободные SCP-роли. Одна смена на игрока за раунд.
/// </summary>
public sealed class ScpSwapFeature
{
    private static readonly RoleTypeId[] SwapPool =
    {
        RoleTypeId.Scp049, RoleTypeId.Scp096, RoleTypeId.Scp106,
        RoleTypeId.Scp173, RoleTypeId.Scp3114, RoleTypeId.Scp939
    };

    private readonly ScpSwapConfig _config;
    private readonly System.Collections.Generic.HashSet<string> _swappedPlayers = new();
    private DateTime _roundStartTime = DateTime.UtcNow;

    public ScpSwapFeature(ScpSwapConfig config)
    {
        _config = config;
    }

    public void OnRoundStarted()
    {
        _swappedPlayers.Clear();
        _roundStartTime = DateTime.UtcNow;
    }

    public void OnWaitingForPlayers() => _swappedPlayers.Clear();

    public void OnLeft(LeftEventArgs ev)
    {
        if (ev.Player != null)
            _swappedPlayers.Remove(ev.Player.UserId);
    }

    public void OnRestartingRound() => _swappedPlayers.Clear();

    public void TrySwap(Player player)
    {
        if (!_config.IsEnabled || player == null || !player.IsVerified || player.IsNPC)
            return;

        if ((DateTime.UtcNow - _roundStartTime).TotalSeconds > Math.Max(5f, _config.WindowSeconds))
        {
            player.ShowZoneHint(HintZone.Notification,
                $"<color=#f87171>Время на смену роли истекло (доступно первые {(int)_config.WindowSeconds} сек. раунда).</color>",
                3f, "scpswap", 22);
            return;
        }

        if (_swappedPlayers.Contains(player.UserId))
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#facc15>Вы уже меняли роль в этом раунде.</color>", 2.5f, "scpswap", 22);
            return;
        }

        if (!player.IsScp || player.Role.Type is RoleTypeId.Scp079 or RoleTypeId.Scp0492)
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#f87171>Смена доступна только для основных SCP-ролей.</color>", 3f, "scpswap", 22);
            return;
        }

        var occupied = Player.List
            .Where(p => p != null && p.IsAlive && p.UserId != player.UserId && IsMainScp(p.Role.Type))
            .Select(p => p.Role.Type)
            .ToHashSet();

        var available = SwapPool.Where(r => !occupied.Contains(r) && r != player.Role.Type).ToList();
        if (available.Count == 0)
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#f87171>Все SCP-роли заняты — менять не на кого.</color>", 3f, "scpswap", 22);
            return;
        }

        var newRole = available[UnityEngine.Random.Range(0, available.Count)];
        string oldRoleName = player.Role.Type.ToRussian();
        player.Role.Set(newRole, Exiled.API.Enums.SpawnReason.None);
        _swappedPlayers.Add(player.UserId);

        player.ShowZoneHint(HintZone.TopCenter,
            $"<color=#c084fc><b>🔄 Вы сменили роль: {oldRoleName} → {newRole.ToRussian()}</b></color>", 4f, "scpswap", 24);

        // Сообщаем остальным SCP о новом составе
        foreach (var scp in Player.List.Where(p => p != null && p.Role.Side == Side.Scp && p.UserId != player.UserId))
            scp.ShowZoneHint(HintZone.Notification,
                $"<color=#c084fc>{player.Nickname} теперь {newRole.ToRussian()}</color>", 3f, "scpswap", 20);
    }

    private static bool IsMainScp(RoleTypeId role) => SwapPool.Contains(role);
}

/// <summary>
/// .swap — сменить свою SCP-роль в начале раунда.
/// </summary>
[CommandHandler(typeof(ClientCommandHandler))]
public sealed class SwapCommand : ICommand
{
    public string Command => "swap";
    public string[] Aliases => new[] { "scp swap", "changescp" };
    public string Description => "Сменить свою SCP-роль (в начале раунда)";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (Player.Get(sender) is not { } player)
        {
            response = "Вы не игрок.";
            return false;
        }

        NoRulesPlugin.Instance?.ScpSwap?.TrySwap(player);
        response = string.Empty;
        return true;
    }
}
