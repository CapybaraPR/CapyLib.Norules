using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.ServerSpecific;
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
    public int TargetPlayerIndex { get; set; } = 0;
}

/// <summary>
/// Полнофункциональная система свободного наблюдателя (Vanish).
/// 1. Вход разрешен ТОЛЬКО из роли Spectator, выход возвращает в Spectator.
/// 2. Спавн в башне на Поверхности (Surface Tower) в роли Tutorial.
/// 3. Выдача монетки телепортации по игрокам (ЛКМ / подбрасывание) и карты Хаоса (для змейки).
/// 4. Невидимость, ноклип, мут голоса, полный игнор SCP-173 / SCP-096, запрет взаимодействия с миром.
/// 5. Авто-спавн в волнах подкрепления МОГ / Хаос.
/// </summary>
public sealed class VanishFeature
{
    private static readonly ConcurrentDictionary<int, VanishSavedState> VanishedStates = new();
    private static readonly Vector3 TowerSpawnPosition = new(39.2f, 1014.5f, -31.8f);

    public static bool IsVanished(Player? player) => player != null && VanishedStates.ContainsKey(player.Id);

    public void Enable()
    {
        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
    }

    public void Disable()
    {
        AssKeybinds.OnKeybindPressed -= OnKeybindPressed;
    }

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
            NoclipEnabled = player.IsNoclipEnabled,
            TargetPlayerIndex = 0
        };

        VanishedStates[player.Id] = state;

        // 1. Переводим в роль Tutorial и спавним в башне на Поверхности
        player.Role.Set(RoleTypeId.Tutorial);
        player.Position = TowerSpawnPosition;

        // 2. Выдаем монетку телепортации и карту Хаоса (для игры в змейку)
        player.ClearInventory();
        player.AddItem(ItemType.Coin);
        player.AddItem(ItemType.KeycardChaosInsurgency);

        // 3. Скрываем игрока от всех через сетевые пакеты (ChangeAppearance → Spectator)
        //    Это прячет модель персонажа на уровне сети — никакой "шапки невидимости"
        Capy.Core.Extensions.NetworkExtensions.ChangeAppearance(player, RoleTypeId.Spectator, true);
        player.IsGodModeEnabled = true;
        player.IsBypassModeEnabled = false;

        // 4. Разрешение NoClip и включение свободного полёта
        player.IsNoclipPermitted = true;
        player.IsNoclipEnabled = true;

        // 5. Заглушение голосового чата (чтобы живые игроки не слышали)
        player.IsMuted = true;

        // 6. Оповещение в HUD
        player.ShowZoneHint(
            HintZone.TopCenter,
            "<color=#38bdf8><b>👻 [СВОБОДНЫЙ НАБЛЮДАТЕЛЬ] ВКЛЮЧЕН</b></color>\n<color=#c2c2c2>Спавн: <color=#ffa94e>Башня</color> • 🪙 ЛКМ/ПКМ: <color=#a3e635>Телепорт к игрокам</color> • 💳 Карта: <color=#a3e635>Змейка</color></color>",
            6.0f,
            "vanish_hud",
            22
        );
    }

    public void DisableVanish(Player player, bool respawnWave = false)
    {
        if (!VanishedStates.TryRemove(player.Id, out var state))
            return;

        // 1. Очистка инвентаря и снятие состояния
        player.ClearInventory();
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

    public void OnFlippingCoin(FlippingCoinEventArgs ev)
    {
        if (ev.Player == null || !IsVanished(ev.Player))
            return;

        ev.IsAllowed = false;
        TeleportToPlayer(ev.Player, 1);
    }

    private void OnKeybindPressed(Player player, CustomKeybind keybind)
    {
        if (player == null || !IsVanished(player)) return;

        if (player.CurrentItem?.Type == ItemType.Coin)
        {
            if (keybind == CustomKeybind.Lmb)
                TeleportToPlayer(player, 1);
            else if (keybind == CustomKeybind.Rmb)
                TeleportToPlayer(player, -1);
        }
    }

    /// <summary>
    /// Телепортирует свободного наблюдателя к следующему (direction=1) или предыдущему (direction=-1) живому игроку.
    /// </summary>
    public void TeleportToPlayer(Player player, int direction)
    {
        if (player == null || !IsVanished(player)) return;

        var alivePlayers = Player.List
            .Where(p => p != null && p.IsConnected && p.IsAlive && !IsVanished(p) && p.Role.Type != RoleTypeId.Spectator && p.Role.Type != RoleTypeId.None)
            .OrderBy(p => p.Id)
            .ToList();

        if (alivePlayers.Count == 0)
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#ff4444><b>Нет живых игроков для наблюдения.</b></color>", 2.0f, "spectator_tp", 20);
            return;
        }

        var state = VanishedStates.GetOrAdd(player.Id, _ => new VanishSavedState());
        state.TargetPlayerIndex = ((state.TargetPlayerIndex + direction) % alivePlayers.Count + alivePlayers.Count) % alivePlayers.Count;
        var target = alivePlayers[state.TargetPlayerIndex];

        player.Position = target.Position + Vector3.up * 0.6f;

        string hex = ColorUtility.ToHtmlStringRGB(target.Role.Color);
        string arrow = direction > 0 ? "▶" : "◀";
        player.ShowZoneHint(
            HintZone.Notification,
            $"<size=20><b><color=#38bdf8>{arrow} [ ТЕЛЕПОРТ НАБЛЮДАТЕЛЯ ] {arrow}</color></b>\nИгрок: <b>{target.Nickname}</b> | Роль: <color=#{hex}><b>[{target.Role.Name}]</b></color> ({state.TargetPlayerIndex + 1}/{alivePlayers.Count})\n<color=#888888>ЛКМ → след. | ПКМ → пред.</color></size>",
            2.0f,
            "spectator_tp",
            20
        );
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

    // --- Блокировка всех взаимодействий с миром ---
    public void OnDroppingItem(DroppingItemEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnPickingUpItem(PickingUpItemEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnDroppingAmmo(DroppingAmmoEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnShooting(ShootingEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnHurting(HurtingEventArgs ev)
    {
        if (IsVanished(ev.Attacker) || IsVanished(ev.Player))
        {
            ev.IsAllowed = false;
            ev.Amount = 0f;
        }
    }

    public void OnInteractingDoor(InteractingDoorEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnInteractingLocker(InteractingLockerEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnInteractingElevator(InteractingElevatorEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnOpeningGenerator(OpeningGeneratorEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnUnlockingGenerator(UnlockingGeneratorEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnActivatingGenerator(ActivatingGeneratorEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnStoppingGenerator(StoppingGeneratorEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnScp173AddingObserver(AddingObserverEventArgs ev)
    {
        if (IsVanished(ev.Observer)) ev.IsAllowed = false;
    }

    public void OnScp096AddingTarget(AddingTargetEventArgs ev)
    {
        if (IsVanished(ev.Target)) ev.IsAllowed = false;
    }
}
