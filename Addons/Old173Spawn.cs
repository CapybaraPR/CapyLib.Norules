using System;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Doors;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Addons;

/// <summary>
/// Спавн SCP-173 («Печеньки») в старую оригинальную камеру содержания (LCZ 173).
/// Двери камеры заблокированы первые 15 секунд от начала раунда, после чего автоматически открываются.
/// Сообщение и обратный отсчёт выводятся ТОЛЬКО игроку, заспавненному за SCP-173 в начале раунда.
/// При спавне через админ-панель посреди раунда игрок просто спавнится на старых координатах без блокировки дверей.
/// </summary>
public sealed class Old173SpawnFeature
{
    private readonly Old173SpawnConfig _config;
    private bool _enabled;
    private bool _roundStarted;
    private DateTime _roundStartTime = DateTime.MinValue;
    private CoroutineHandle _doorCoroutine;
    private CoroutineHandle _hintCoroutine;

    public Old173SpawnFeature(Old173SpawnConfig config)
    {
        _config = config;
    }

    public void Enable()
    {
        if (_enabled || !_config.IsEnabled) return;
        _enabled = true;

        Exiled.Events.Handlers.Player.Spawned += OnSpawned;
        Exiled.Events.Handlers.Player.ChangingRole += OnChangingRole;
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
        Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        Exiled.Events.Handlers.Player.Spawned -= OnSpawned;
        Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;

        ResetState();
    }

    private void OnWaitingForPlayers() => ResetState();

    private void OnRestartingRound() => ResetState();

    private void ResetState()
    {
        _roundStarted = false;
        _roundStartTime = DateTime.MinValue;

        if (_doorCoroutine.IsRunning)
            Timing.KillCoroutines(_doorCoroutine);

        if (_hintCoroutine.IsRunning)
            Timing.KillCoroutines(_hintCoroutine);
    }

    private void OnRoundStarted()
    {
        _roundStarted = true;
        _roundStartTime = DateTime.UtcNow;

        LockChamberDoorsAtRoundStart();
    }

    private void OnSpawned(SpawnedEventArgs ev)
    {
        if (ev.Player == null) return;
        if (ev.Player.Role.Type == RoleTypeId.Scp173)
        {
            TeleportToOldSpawn(ev.Player);
        }
    }

    private void OnChangingRole(ChangingRoleEventArgs ev)
    {
        if (ev.Player == null || ev.NewRole != RoleTypeId.Scp173) return;

        TeleportToOldSpawn(ev.Player);
    }

    private void TeleportToOldSpawn(Player player)
    {
        Timing.CallDelayed(_config.TeleportDelay, () =>
        {
            try
            {
                if (player == null || !player.IsConnected || player.Role.Type != RoleTypeId.Scp173)
                    return;

                var room = Room.List.FirstOrDefault(r => r.Type == RoomType.Lcz173);
                if (room == null)
                {
                    Log.Warn("[Old173Spawn] Комната Lcz173 не найдена на карте.");
                    return;
                }

                Vector3 targetPos = room.Position + room.Rotation * _config.SpawnOffset;
                Quaternion targetRot = room.Rotation * Quaternion.Euler(_config.SpawnRotation);

                player.Position = targetPos;
                player.Rotation = targetRot;

                Log.Debug($"[Old173Spawn] SCP-173 ({player.Nickname}) телепортирован на старый спавн: {targetPos}");

                // Проверяем: заспавнен ли 173 в начале раунда?
                bool isRoundStartSpawn = _roundStarted &&
                    (DateTime.UtcNow - _roundStartTime).TotalSeconds <= (_config.DoorUnlockSeconds + 5f);

                if (isRoundStartSpawn)
                {
                    StartCountdownForRoundStart173(player);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Old173Spawn] Ошибка телепортации SCP-173: {ex}");
            }
        });
    }

    private void LockChamberDoorsAtRoundStart()
    {
        if (!_config.LockDoorOnSpawn) return;

        var room = Room.List.FirstOrDefault(r => r.Type == RoomType.Lcz173);
        if (room == null) return;

        var doors = room.Doors.Where(d =>
            d.Type == DoorType.Scp173Gate ||
            d.Type == DoorType.Scp173Bottom ||
            d.Type == DoorType.Scp173NewGate ||
            d.Type == DoorType.Scp173Connector ||
            d.Type == DoorType.Scp173Armory).ToList();

        if (doors.Count == 0)
            doors = room.Doors.ToList();

        foreach (var door in doors)
        {
            try
            {
                door.IsOpen = false;
                door.Lock(_config.DoorUnlockSeconds, DoorLockType.AdminCommand);
            }
            catch { }
        }

        if (_doorCoroutine.IsRunning)
            Timing.KillCoroutines(_doorCoroutine);

        _doorCoroutine = Timing.CallDelayed(_config.DoorUnlockSeconds, () =>
        {
            foreach (var door in doors)
            {
                try
                {
                    door.Unlock();
                    door.IsOpen = true;
                }
                catch { }
            }
        });
    }

    private void StartCountdownForRoundStart173(Player player)
    {
        float elapsed = (float)(DateTime.UtcNow - _roundStartTime).TotalSeconds;
        float remaining = _config.DoorUnlockSeconds - elapsed;

        if (remaining <= 0f) return;

        if (_hintCoroutine.IsRunning)
            Timing.KillCoroutines(_hintCoroutine);

        _hintCoroutine = Timing.RunCoroutine(CountdownHintCoroutine(player, remaining));
    }

    private IEnumerator<float> CountdownHintCoroutine(Player player, float seconds)
    {
        float remaining = seconds;

        while (remaining > 0f)
        {
            if (player == null || !player.IsConnected || player.Role.Type != RoleTypeId.Scp173)
                yield break;

            player.ShowZoneHint(HintZone.TopCenter,
                "<color=#ef4444><b>🔒 ДВЕРИ КАМЕРЫ СОДЕРЖАНИЯ ЗАБЛОКИРОВАНЫ</b></color>\n" +
                $"<size=75%><color=#c2c2c2>Камера откроется через: <color=#ffd285><b>{Mathf.CeilToInt(remaining)} сек</b></color></color></size>",
                1.1f, "old173_door", 20);

            yield return Timing.WaitForSeconds(1f);
            remaining -= 1f;
        }

        if (player != null && player.IsConnected && player.Role.Type == RoleTypeId.Scp173)
        {
            player.ShowZoneHint(HintZone.TopCenter,
                "<color=#4ade80><b>🔓 ДВЕРИ КАМЕРЫ ОТКРЫТЫ!</b></color>\n" +
                "<size=75%><color=#c2c2c2>Вы свободны — нарушьте условия содержания!</color></size>",
                4f, "old173_door", 20);
        }
    }
}

