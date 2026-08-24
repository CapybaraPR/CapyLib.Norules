using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.API.DiscordBridge;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Capy.NoRules.Features.Models;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using InventorySystem.Items;
using MEC;
using PlayerRoles;
using Exiled.API.Enums;

namespace Capy.NoRules.Features;

/// <summary>
/// Система опыта и уровней игроков (порт Hazbin.NoRules.PlayerXp на EXILED).
/// Опыт начисляется за игровые действия, уровень отображается над ником и
/// синхронизируется с Discord через Bridge API (v1/xp).
/// </summary>
public sealed class PlayerXpFeature
{
    private readonly PlayerXpConfig _config;
    private XpDatabase? _database;
    private bool _enabled;

    // Активные корутины начисления за жизнь: userId -> handle
    private readonly ConcurrentDictionary<string, CoroutineHandle> _aliveCoroutines = new();

    public PlayerXpFeature(PlayerXpConfig config)
    {
        _config = config;
    }

    public XpDatabase? Database => _database;

    public void Enable()
    {
        if (_enabled || !_config.IsEnabled) return;
        _enabled = true;

        string dir = System.IO.Path.Combine(Exiled.API.Features.Paths.Configs, "CapyLib", "PlayerXp");
        _database = new XpDatabase(dir);

        Exiled.Events.Handlers.Player.Joined += OnJoined;
        Exiled.Events.Handlers.Player.Left += OnLeft;
        Exiled.Events.Handlers.Player.ChangingRole += OnChangingRole;
        Exiled.Events.Handlers.Player.Escaped += OnEscaped;
        Exiled.Events.Handlers.Player.Died += OnDied;
        Exiled.Events.Handlers.Player.PickingUpItem += OnPickingUpItem;
        Exiled.Events.Handlers.Player.UsedItem += OnUsedItem;
        Exiled.Events.Handlers.Player.InteractingLocker += OnInteractingLocker;
        Exiled.Events.Handlers.Player.InteractingDoor += OnInteractingDoor;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;

        RegisterBridgeHooks();
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        UnregisterBridgeHooks();

        Exiled.Events.Handlers.Player.Joined -= OnJoined;
        Exiled.Events.Handlers.Player.Left -= OnLeft;
        Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;
        Exiled.Events.Handlers.Player.Escaped -= OnEscaped;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        Exiled.Events.Handlers.Player.PickingUpItem -= OnPickingUpItem;
        Exiled.Events.Handlers.Player.UsedItem -= OnUsedItem;
        Exiled.Events.Handlers.Player.InteractingLocker -= OnInteractingLocker;
        Exiled.Events.Handlers.Player.InteractingDoor -= OnInteractingDoor;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;

        foreach (var handle in _aliveCoroutines.Values)
            Timing.KillCoroutines(handle);

        _aliveCoroutines.Clear();
        _database?.Dispose();
        _database = null;
    }

    // --- Начисление ---

    /// <summary>
    /// Начисляет опыт игроку (с делителем и множителем тега) и обновляет HUD.
    /// </summary>
    public void GiveXp(Player player, float exp)
    {
        if (_database == null || player == null || player.DoNotTrack || !player.IsConnected)
            return;

        exp /= Math.Max(0.01f, _config.XpDivisor);

        if (!string.IsNullOrEmpty(_config.SpecialTag) &&
            player.Nickname.IndexOf(_config.SpecialTag, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            exp *= _config.TaggedMultiplier;
        }

        _database.EnsurePlayer(player.UserId, player.Nickname);
        _database.GiveXp(player.UserId, exp);

        player.ShowZoneHint(HintZone.Notification,
            $"<b>Вы получили <color=#ffe91f>{Math.Round(exp, 2)}</color> опыта!</b>",
            2.4f, "xp", 24);

        ApplyLevelBadge(player);
    }

    /// <summary>
    /// Ставит бейдж уровня над ником игрока.
    /// </summary>
    public void ApplyLevelBadge(Player player)
    {
        try
        {
            if (player == null || !player.IsConnected) return;
            if (_database == null) return;

            string nickname = player.Nickname.Replace('[', '(').Replace(']', ')');

            if (player.DoNotTrack)
            {
                player.CustomInfo = $"(<color={_config.UnknownColorHex}>{_config.UnknownText}</color>)\n{nickname}";
            }
            else
            {
                var level = _database.GetLevel(player.UserId);
                string text = level != null
                    ? $"<color={level.ColorHex}>{level.Text}</color>"
                    : $"<color={_config.UnknownColorHex}>{_config.UnknownText}</color>";

                player.CustomInfo = $"({text})\n{nickname}";
            }

            player.InfoArea = (PlayerInfoArea)~(int)PlayerInfoArea.Nickname;
        }
        catch (Exception ex)
        {
            Log.Debug($"[PlayerXp] ApplyLevelBadge: {ex.Message}");
        }
    }

    // --- События ---

    private void OnJoined(JoinedEventArgs ev)
    {
        if (_database == null || ev.Player == null || ev.Player.DoNotTrack)
            return;

        _database.EnsurePlayer(ev.Player.UserId, ev.Player.Nickname);

        Timing.CallDelayed(0.45f, () => ApplyLevelBadge(ev.Player));
    }

    private void OnLeft(LeftEventArgs ev)
    {
        if (ev.Player == null) return;

        if (_aliveCoroutines.TryRemove(ev.Player.UserId, out var handle))
            Timing.KillCoroutines(handle);
    }

    private void OnChangingRole(ChangingRoleEventArgs ev)
    {
        if (ev.Player == null || _database == null) return;

        ApplyLevelBadge(ev.Player);

        if (ev.Player.DoNotTrack)
            return;

        // Запускаем тик жизни при переходе в живую человеческую роль
        if (ev.NewRole != RoleTypeId.None && ev.NewRole != RoleTypeId.Spectator &&
            ev.NewRole != RoleTypeId.Overwatch && !IsScpRole(ev.NewRole))
        {
            string tag = $"xpAlive.{ev.Player.UserId}";
            _aliveCoroutines[ev.Player.UserId] = Timing.RunCoroutine(AliveCoroutine(ev.Player), tag);
        }
    }

    private static bool IsScpRole(RoleTypeId role)
    {
        return role is RoleTypeId.Scp049 or RoleTypeId.Scp0492 or RoleTypeId.Scp079 or RoleTypeId.Scp096
            or RoleTypeId.Scp106 or RoleTypeId.Scp173 or RoleTypeId.Scp3114 or RoleTypeId.Scp939;
    }

    /// <summary>
    /// Награда за успешный побег (нативное событие Escaped).
    /// </summary>
    private void OnEscaped(EscapedEventArgs ev)
    {
        if (ev.Player == null || ev.Player.DoNotTrack) return;

        if (ev.Player.IsCuffed && ev.Player.Cuffer != null)
            GiveXp(ev.Player.Cuffer, 100f);

        GiveXp(ev.Player, 150f);
    }

    private void OnDied(DiedEventArgs ev)
    {
        if (ev.Player == null) return;

        if (_aliveCoroutines.TryRemove(ev.Player.UserId, out var handle))
            Timing.KillCoroutines(handle);

        if (ev.Attacker == null || ev.Attacker.UserId == ev.Player.UserId)
            return;

        if (ev.Player.Role.Side == Side.Scp)
            GiveXp(ev.Attacker, 200f);
        else if (ev.Attacker.Role.Side == Side.Scp)
            GiveXp(ev.Attacker, 70f);
        else
            GiveXp(ev.Attacker, 100f);
    }

    private void OnPickingUpItem(PickingUpItemEventArgs ev)
    {
        if (ev.Player == null || ev.Pickup == null || ev.Player.DoNotTrack)
            return;

        if (ev.Pickup.Type is ItemType.MicroHID or ItemType.Jailbird or ItemType.ParticleDisruptor)
            GiveXp(ev.Player, 0.7f);
        else if (ev.Pickup.Category == ItemCategory.SCPItem)
            GiveXp(ev.Player, 5f);
        else if (ev.Pickup.Category == ItemCategory.SpecialWeapon)
            GiveXp(ev.Player, 7.5f);
        else if (ev.Pickup.Category == ItemCategory.Firearm)
            GiveXp(ev.Player, 0.5f);
        else if (ev.Pickup.Category == ItemCategory.Keycard)
            GiveXp(ev.Player, 0.2f);
        else
            GiveXp(ev.Player, 0.1f);
    }

    private void OnUsedItem(UsedItemEventArgs ev)
    {
        if (ev.Player == null || ev.Item == null || ev.Player.DoNotTrack)
            return;

        GiveXp(ev.Player, ev.Item.Category == ItemCategory.SCPItem ? 10f : 0.5f);
    }

    private void OnInteractingLocker(InteractingLockerEventArgs ev)
    {
        if (ev.Player?.DoNotTrack == false)
            GiveXp(ev.Player, 0.5f);
    }

    private void OnInteractingDoor(InteractingDoorEventArgs ev)
    {
        if (ev.Door != null && ev.Player?.DoNotTrack == false)
            GiveXp(ev.Player, 0.5f);
    }

    private void OnRestartingRound()
    {
        foreach (var handle in _aliveCoroutines.Values)
            Timing.KillCoroutines(handle);

        _aliveCoroutines.Clear();
        _database?.Dispose();
        _database = new XpDatabase(System.IO.Path.Combine(Exiled.API.Features.Paths.Configs, "CapyLib", "PlayerXp"));
    }

    private IEnumerator<float> AliveCoroutine(Player player)
    {
        while (player.IsConnected && player.IsAlive)
        {
            yield return Timing.WaitForSeconds(Math.Max(5f, _config.AliveTickSeconds));

            if (!player.IsAlive) break;

            GiveXp(player, _config.AliveXpAmount);
        }
    }

    // --- Интеграция с Bridge API (Discord) ---

    private void RegisterBridgeHooks()
    {
        BridgeXpRegistry.GetXp = userId =>
        {
            if (_database == null) return null;
            float xp = _database.GetXp(userId);
            var level = _database.GetLevel(userId);
            return new XpSnapshot
            {
                Xp = xp,
                LevelText = level?.Text ?? _config.UnknownText,
                LevelColor = level?.ColorHex ?? _config.UnknownColorHex
            };
        };

        BridgeXpRegistry.GetLeaderboard = count =>
        {
            if (_database == null) return null;

            return _database.GetTop(count)
                .Select(e => new XpLeaderboardEntry
                {
                    UserId = e.Record.UserId,
                    Nickname = e.Record.Nickname,
                    Xp = e.Record.Xp,
                    LevelText = e.Level.Text,
                    LevelColor = e.Level.ColorHex
                })
                .ToList();
        };
    }

    private void UnregisterBridgeHooks()
    {
        BridgeXpRegistry.GetXp = null;
        BridgeXpRegistry.GetLeaderboard = null;
    }
}
