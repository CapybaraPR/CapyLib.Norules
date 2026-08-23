using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Extensions;
using CustomPlayerEffects;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Scp096;
using Exiled.Events.EventArgs.Scp173;
using Exiled.Events.EventArgs.Server;
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Features;

/// <summary>
/// Сохраненное состояние игрока до входа в режим свободного наблюдателя (Vanish).
/// </summary>
public sealed class VanishSavedState
{
    public Vector3 Position { get; set; }
    public bool NoclipPermitted { get; set; }
    public bool NoclipEnabled { get; set; }
}

/// <summary>
/// Полнофункциональная система свободного наблюдателя / скрытного режима администратора (Vanish).
/// Вход разрешен ТОЛЬКО из роли Spectator, выход возвращает в Spectator.
/// Включает: невидимость, ноклип, роль Tutorial (не мешает раунду), мут микрофона,
/// защиту от SCP-173 (не стопит) и SCP-096 (не агрит), запрет любого взаимодействия с миром,
/// а также автоматический спавн в волнах подкрепления МОГ / Хаос.
/// </summary>
public sealed class VanishFeature
{
    private static readonly ConcurrentDictionary<int, VanishSavedState> VanishedStates = new();

    public static bool IsVanished(Player? player) => player != null && VanishedStates.ContainsKey(player.Id);

    public bool Toggle(Player player, out string response)
    {
        if (player == null || !player.IsConnected)
        {
            response = "Игрок не подключен.";
            return false;
        }

        if (IsVanished(player))
        {
            DisableVanish(player);
            response = $"<color=yellow>[VANISH]</color> Режим свободного наблюдателя для <b>{player.Nickname}</b> выключен (возврат в Spectator).";
            return true;
        }
        else
        {
            // Разрешено входить ТОЛЬКО из роли Spectator!
            if (player.Role.Type != RoleTypeId.Spectator)
            {
                response = "<color=red>[ОШИБКА]</color> Режим свободного наблюдателя (Vanish) можно включить <b>только находясь в наблюдателях (Spectator)</b>.";
                return false;
            }

            EnableVanish(player);
            response = $"<color=green>[VANISH]</color> Режим свободного наблюдателя для <b>{player.Nickname}</b> успешно включен.";
            return true;
        }
    }

    private void EnableVanish(Player player)
    {
        var state = new VanishSavedState
        {
            Position = player.Position,
            NoclipPermitted = player.IsNoclipPermitted,
            NoclipEnabled = player.IsNoclipEnabled
        };

        VanishedStates[player.Id] = state;

        // 1. Переводим в роль Tutorial (не мешает подсчёту живых и завершению раунда)
        player.Role.Set(RoleTypeId.Tutorial);
        player.Position = state.Position + Vector3.up * 0.1f;
        player.ClearInventory();

        // 2. Эффект невидимости и неуязвимость
        player.EnableEffect<Invisible>(999999f, false);
        player.IsGodModeEnabled = true;
        player.IsBypassModeEnabled = false;

        // 3. Разрешение NoClip и включение свободного полёта
        player.IsNoclipPermitted = true;
        player.IsNoclipEnabled = true;

        // 4. Заглушение голосового чата (чтобы игроки не слышали)
        player.IsMuted = true;

        // 5. Оповещение в HUD
        player.ShowZoneHint(
            HintZone.TopCenter,
            "<color=#38bdf8><b>👻 [СВОБОДНЫЙ НАБЛЮДАТЕЛЬ] ВКЛЮЧЕН</b></color>\n<color=#c2c2c2>NoClip: <color=#a3e635>ВКЛ</color> • Голос: <color=#f87171>ЗАГЛУШЕН</color> • SCP: <color=#a3e635>НЕ РЕАГИРУЮТ</color> • Спавн в волнах: <color=#a3e635>АКТИВЕН</color></color>",
            6.0f,
            "vanish_hud",
            22
        );
    }

    public void DisableVanish(Player player, bool respawnWave = false)
    {
        if (!VanishedStates.TryRemove(player.Id, out var state))
            return;

        // 1. Снятие невидимости и ограничений
        player.DisableEffect<Invisible>();
        player.IsGodModeEnabled = false;
        player.IsNoclipPermitted = state.NoclipPermitted;
        player.IsNoclipEnabled = false;
        player.IsMuted = false;

        // 2. Если выход НЕ по волне возрождения — возвращаем строго в Spectator
        if (!respawnWave)
        {
            player.Role.Set(RoleTypeId.Spectator);
            player.ShowZoneHint(
                HintZone.TopCenter,
                "<color=#ff4444><b>👻 [СВОБОДНЫЙ НАБЛЮДАТЕЛЬ] ВЫКЛЮЧЕН</b></color>",
                3.5f,
                "vanish_hud",
                22
            );
        }
    }

    public void OnRespawningTeam(RespawningTeamEventArgs ev)
    {
        if (ev == null || !ev.IsAllowed || ev.Players == null) return;

        foreach (var id in VanishedStates.Keys.ToList())
        {
            var player = Player.Get(id);
            if (player != null && player.IsConnected)
            {
                if (ev.Players.Count < ev.MaximumRespawnAmount && !ev.Players.Contains(player))
                {
                    ev.Players.Add(player);
                    DisableVanish(player, respawnWave: true);
                }
            }
        }
    }

    public void OnPlayerLeft(LeftEventArgs ev)
    {
        if (ev.Player != null)
            VanishedStates.TryRemove(ev.Player.Id, out _);
    }

    public void OnRoundRestarted()
    {
        VanishedStates.Clear();
    }

    // --- Запреты взаимодействия с миром для свободного наблюдателя ---

    public void OnInteractingDoor(InteractingDoorEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnInteractingLocker(InteractingLockerEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnInteractingElevator(InteractingElevatorEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnOpeningGenerator(OpeningGeneratorEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnUnlockingGenerator(UnlockingGeneratorEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnActivatingGenerator(ActivatingGeneratorEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnStoppingGenerator(StoppingGeneratorEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnPickingUpItem(PickingUpItemEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnDroppingItem(DroppingItemEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnDroppingAmmo(DroppingAmmoEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnShooting(ShootingEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnHurting(HurtingEventArgs ev)
    {
        if (IsVanished(ev.Attacker))
            ev.IsAllowed = false;

        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnScp173AddingObserver(AddingObserverEventArgs ev)
    {
        if (IsVanished(ev.Player))
            ev.IsAllowed = false;
    }

    public void OnScp096AddingTarget(AddingTargetEventArgs ev)
    {
        if (IsVanished(ev.Target))
            ev.IsAllowed = false;
    }
}
