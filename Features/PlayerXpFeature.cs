using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Capy.API.DiscordBridge;
using Capy.Core.Database.Models;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Capy.NoRules.Features.Models;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using InventorySystem.Items;
using MEC;
using PlayerRoles;

namespace Capy.NoRules.Features;

/// <summary>
/// Система опыта и уровней игроков (порт Hazbin.NoRules.PlayerXp на EXILED).
/// Опыт начисляется за игровые действия, уровень отображается над ником и
/// синхронизируется с Discord через Bridge API (v1/xp).
/// Хранение — общая БД CapyLib (PlayerDataModel.Xp), вместе со статистикой и .top.
/// </summary>
public sealed class PlayerXpFeature
{
    private readonly PlayerXpConfig _config;
    private XpLevelStore? _levels;
    private bool _enabled;

    // Активные корутины начисления за жизнь: userId -> handle
    private readonly ConcurrentDictionary<string, CoroutineHandle> _aliveCoroutines = new();

    public PlayerXpFeature(PlayerXpConfig config)
    {
        _config = config;
    }

    public XpLevelStore? Levels => _levels;

    private static Capy.Core.Database.IDatabaseProvider? Db => CapyPlugin.Instance?.Database;

    /// <summary>Включена ли система (для команд).</summary>
    public bool IsEnabled() => _enabled;

    public void Enable()
    {
        if (_enabled || !_config.IsEnabled) return;
        _enabled = true;

        string dir = Path.Combine(Exiled.API.Features.Paths.Configs, "CapyLib", "PlayerXp");
        _levels = new XpLevelStore(dir);

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

        CleanupInvalidRecords();

        RegisterBridgeHooks();
    }

    /// <summary>
    /// Удаляет записи с некорректными UserId (созданные старыми командами без резолва игрока).
    /// Валидный UserId всегда содержит '@' (например, "76561198...@steam").
    /// </summary>
    private static void CleanupInvalidRecords()
    {
        try
        {
            var db = Db;
            if (db == null) return;

            foreach (var model in db.GetAllPlayers())
            {
                if (!string.IsNullOrEmpty(model.Id) && !model.Id.Contains('@'))
                {
                    db.DeletePlayer(model.Id);
                    Log.Warn($"[PlayerXp] Удалена битая запись опыта: '{model.Id}' ({model.Xp:F0} XP)");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[PlayerXp] Ошибка очистки записей: {ex.Message}");
        }
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
        _levels = null;
    }

    // --- Начисление ---

    /// <summary>
    /// Начисляет опыт игроку (с делителем и множителем тега) и обновляет HUD/БД.
    /// </summary>
    public void GiveXp(Player player, float exp)
    {
        if (player == null || player.DoNotTrack || !player.IsConnected || !player.IsVerified)
            return;

        exp /= Math.Max(0.01f, _config.XpDivisor);

        if (!string.IsNullOrEmpty(_config.SpecialTag) &&
            player.Nickname.IndexOf(_config.SpecialTag, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            exp *= _config.TaggedMultiplier;
        }

        AddRawXp(player.UserId, player.Nickname, exp);

        // Правый нижний угол (зеркально полоске HP); быстрый спам наград просто обновляет текст
        player.ShowZoneHint(HintZone.LowerRight,
            $"<b>Вы получили <color=#ffe91f>+{Math.Round(exp, 2)}</color> опыта!</b>",
            2.5f, "xp", 24);

        ApplyLevelBadge(player);
    }

    private static void AddRawXp(string userId, string nickname, float amount)
    {
        try
        {
            var db = Db;
            if (db == null || string.IsNullOrWhiteSpace(userId)) return;

            PlayerDataModel model = db.GetPlayer(userId) ?? new PlayerDataModel { Id = userId };
            model.LastNickname = !string.IsNullOrWhiteSpace(nickname) ? nickname : model.LastNickname;
            model.Xp += amount;
            model.LastSeen = DateTime.UtcNow;
            db.SavePlayer(model);
        }
        catch (Exception ex)
        {
            Log.Debug($"[PlayerXp] AddRawXp: {ex.Message}");
        }
    }

    public float GetXp(string userId)
    {
        return Db?.GetPlayer(userId)?.Xp ?? 0f;
    }

    public bool HasRecord(string userId)
    {
        return Db?.GetPlayer(userId) != null;
    }

    /// <summary>
    /// Устанавливает точное количество опыта (админ-команда).
    /// </summary>
    public void SetRawXp(string userId, string nickname, float amount)
    {
        try
        {
            var db = Db;
            if (db == null || string.IsNullOrWhiteSpace(userId)) return;

            PlayerDataModel model = db.GetPlayer(userId) ?? new PlayerDataModel { Id = userId };
            model.LastNickname = !string.IsNullOrWhiteSpace(nickname) ? nickname : model.LastNickname;
            model.Xp = amount;
            model.LastSeen = DateTime.UtcNow;
            db.SavePlayer(model);
        }
        catch (Exception ex)
        {
            Log.Debug($"[PlayerXp] SetRawXp: {ex.Message}");
        }
    }

    public XpLevel? GetLevelFor(string userId)
    {
        if (_levels == null || Db == null) return null;

        var model = Db.GetPlayer(userId);
        return model == null ? null : _levels.GetLevelForXp(model.Xp);
    }

    /// <summary>
    /// Ставит бейдж уровня над ником игрока.
    /// </summary>
    public void ApplyLevelBadge(Player player)
    {
        try
        {
            if (player == null || !player.IsConnected || _levels == null) return;

            string nickname = player.Nickname.Replace('[', '(').Replace(']', ')');

            if (player.DoNotTrack)
            {
                player.CustomInfo = $"(<color={_config.UnknownColorHex}>{_config.UnknownText}</color>)\n{nickname}";
            }
            else
            {
                var level = GetLevelFor(player.UserId);
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
        if (ev.Player == null || ev.Player.DoNotTrack) return;

        AddRawXp(ev.Player.UserId, ev.Player.Nickname, 0f); // создаёт запись и обновляет ник
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
        if (ev.Player == null) return;

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
            if (_levels == null || Db == null) return null;

            var model = Db.GetPlayer(userId);
            if (model == null) return null;

            var level = _levels.GetLevelForXp(model.Xp);
            return new XpSnapshot
            {
                Xp = model.Xp,
                LevelText = level.Text,
                LevelColor = level.ColorHex
            };
        };

        BridgeXpRegistry.GetLeaderboard = count =>
        {
            if (_levels == null || Db == null) return null;

            return Db.GetTopPlayers(nameof(PlayerDataModel.Xp), count)
                .Select(m => new XpLeaderboardEntry
                {
                    UserId = m.Id,
                    Nickname = m.LastNickname,
                    Xp = m.Xp,
                    LevelText = _levels.GetLevelForXp(m.Xp).Text,
                    LevelColor = _levels.GetLevelForXp(m.Xp).ColorHex
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
