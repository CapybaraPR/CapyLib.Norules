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
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Features;

/// <summary>
/// Сохраненное состояние игрока до входа в режим Vanish.
/// </summary>
public sealed class VanishSavedState
{
    public RoleTypeId Role { get; set; }
    public Vector3 Position { get; set; }
    public List<ItemType> Items { get; set; } = new();
    public Dictionary<AmmoType, ushort> Ammo { get; set; } = new();
    public bool GodMode { get; set; }
    public bool NoclipPermitted { get; set; }
    public bool NoclipEnabled { get; set; }
    public bool Muted { get; set; }
}

/// <summary>
/// Полнофункциональная система скрытного режима администратора (Vanish).
/// Включает: невидимость, ноклип, роль Tutorial (не мешает раунду), мут микрофона,
/// защиту от SCP-173 (не стопит) и SCP-096 (не агрит), а также запрет любого взаимодействия с миром.
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
            response = $"<color=yellow>[VANISH]</color> Режим скрытности для <b>{player.Nickname}</b> выключен.";
            return true;
        }
        else
        {
            EnableVanish(player);
            response = $"<color=green>[VANISH]</color> Режим скрытности для <b>{player.Nickname}</b> успешно включен.";
            return true;
        }
    }

    private void EnableVanish(Player player)
    {
        // 1. Сохраняем предыдущее состояние
        var state = new VanishSavedState
        {
            Role = player.Role.Type,
            Position = player.Position,
            GodMode = player.IsGodModeEnabled,
            NoclipPermitted = player.IsNoclipPermitted,
            NoclipEnabled = player.IsNoclipEnabled,
            Muted = player.IsMuted
        };

        if (player.Items != null)
        {
            state.Items = player.Items.Select(i => i.Type).ToList();
        }

        state.Ammo[AmmoType.Nato9] = player.GetAmmo(AmmoType.Nato9);
        state.Ammo[AmmoType.Nato556] = player.GetAmmo(AmmoType.Nato556);
        state.Ammo[AmmoType.Nato762] = player.GetAmmo(AmmoType.Nato762);
        state.Ammo[AmmoType.Ammo12Gauge] = player.GetAmmo(AmmoType.Ammo12Gauge);
        state.Ammo[AmmoType.Ammo44Cal] = player.GetAmmo(AmmoType.Ammo44Cal);

        VanishedStates[player.Id] = state;

        // 2. Переводим в роль Tutorial (не мешает подсчёту живых и завершению раунда)
        player.Role.Set(RoleTypeId.Tutorial);
        player.Position = state.Position + Vector3.up * 0.1f;
        player.ClearInventory();

        // 3. Эффект невидимости и неуязвимость
        player.EnableEffect<Invisible>(999999f, false);
        player.IsGodModeEnabled = true;
        player.IsBypassModeEnabled = false;

        // 4. Разрешение NoClip и включение полета
        player.IsNoclipPermitted = true;
        player.IsNoclipEnabled = true;

        // 5. Заглушение голосового чата (чтобы игроки не слышали)
        player.IsMuted = true;

        // 6. Оповещение в HUD
        player.ShowZoneHint(
            HintZone.TopCenter,
            "<color=#38bdf8><b>👻 [VANISH] Режим скрытности ВКЛЮЧЕН</b></color>\n<color=#c2c2c2>NoClip: <color=#a3e635>ВКЛ</color> • Голос: <color=#f87171>ЗАГЛУШЕН</color> • SCP: <color=#a3e635>НЕ РЕАГИРУЮТ</color> • Вне раунда (Tutorial)</color>",
            6.0f,
            "vanish_hud",
            22
        );
    }

    private void DisableVanish(Player player)
    {
        if (!VanishedStates.TryRemove(player.Id, out var state))
            return;

        // 1. Снятие невидимости и ограничений
        player.DisableEffect<Invisible>();
        player.IsGodModeEnabled = state.GodMode;
        player.IsNoclipPermitted = state.NoclipPermitted;
        player.IsNoclipEnabled = state.NoclipEnabled;
        player.IsMuted = state.Muted;

        // 2. Возврат исходной роли и инвентаря
        if (state.Role != RoleTypeId.None && state.Role != RoleTypeId.Spectator && state.Role != RoleTypeId.Tutorial)
        {
            player.Role.Set(state.Role);
            player.Position = state.Position;
            player.ClearInventory();

            foreach (var item in state.Items)
            {
                player.AddItem(item);
            }

            foreach (var kvp in state.Ammo)
            {
                player.SetAmmo(kvp.Key, kvp.Value);
            }
        }
        else
        {
            player.Role.Set(RoleTypeId.Spectator);
        }

        // 3. Оповещение в HUD
        player.ShowZoneHint(
            HintZone.TopCenter,
            "<color=#ff4444><b>👻 [VANISH] Режим скрытности ВЫКЛЮЧЕН</b></color>",
            3.5f,
            "vanish_hud",
            22
        );
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

    // --- Запреты взаимодействия с миром для Vanish-игрока ---

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
