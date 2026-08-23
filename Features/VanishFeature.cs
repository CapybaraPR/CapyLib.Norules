using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.Hud.Panels;
using Capy.Engine.ServerSpecific;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Roles;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Scp049;
using Exiled.Events.EventArgs.Scp096;
using Exiled.Events.EventArgs.Scp173;
using Exiled.Events.EventArgs.Scp3114;
using Exiled.Events.EventArgs.Scp330;
using Exiled.Events.EventArgs.Scp914;
using Exiled.Events.EventArgs.Server;
using Exiled.Events.EventArgs.Warhead;
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
/// 3. Выдача монетки телепортации (ЛКМ - след. игрок, ПКМ - пред. игрок) и карты Хаоса (для змейки).
/// 4. Отображение способностей монетки над полоской HP через нативный ItemHudPanel (100% без мерцания).
/// 5. Полное скрытие сетевыми пакетами Mirror (ChangeAppearance -> Spectator), без эффекта шапки.
/// 6. Полный игнор Tesla ворот (не реагируют, не бьют током), SCP-173 и SCP-096.
/// 7. Авто-спавн в волнах подкрепления МОГ / Хаос.
/// </summary>
public sealed class VanishFeature
{
    private static readonly ConcurrentDictionary<int, VanishSavedState> VanishedStates = new();
    private static readonly Vector3 TowerSpawnPosition = new(39.2f, 1014.5f, -31.8f);

    public static bool IsVanished(Player? player) => player != null && VanishedStates.ContainsKey(player.Id);

    public void Enable()
    {
        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
        ItemHudPanel.ExternalItemHudProvider = GetVanishItemHudText;
    }

    public void Disable()
    {
        AssKeybinds.OnKeybindPressed -= OnKeybindPressed;
        if (ItemHudPanel.ExternalItemHudProvider == GetVanishItemHudText)
            ItemHudPanel.ExternalItemHudProvider = null;

        VanishedStates.Clear();
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

        // 3. Скрываем игрока от всех через сетевые пакеты Mirror (GhostMode + ChangeAppearance -> Spectator)
        if (player.Role.Is(out FpcRole fpcRole))
        {
            fpcRole.IsInvisible = true;
        }
        Capy.Core.Extensions.NetworkExtensions.ChangeAppearance(player, RoleTypeId.Spectator, true);
        player.IsGodModeEnabled = true;
        player.IsBypassModeEnabled = false;

        // 4. Разрешение NoClip и включение свободного полёта
        player.IsNoclipPermitted = true;
        player.IsNoclipEnabled = true;

        // 5. Заглушение голосового чата (чтобы живые игроки не слышали)
        player.IsMuted = true;

        // 6. Оповещение вверху экрана
        player.ShowZoneHint(
            HintZone.TopCenter,
            "<color=#38bdf8><b>👻 [СВОБОДНЫЙ НАБЛЮДАТЕЛЬ] ВКЛЮЧЕН</b></color>\n<color=#c2c2c2>Спавн: <color=#ffa94e>Башня</color> • 🪙 ЛКМ/ПКМ: <color=#a3e635>Телепорт к игрокам</color> • ⚡ Тесла: <color=#a3e635>Игнорирует</color></color>",
            6.0f,
            "vanish_hud",
            22
        );
    }

    public void DisableVanish(Player player, bool respawnWave = false)
    {
        if (!VanishedStates.TryRemove(player.Id, out var state))
            return;

        if (player.Role.Is(out FpcRole fpcRole))
        {
            fpcRole.IsInvisible = false;
        }

        player.ClearInventory();
        player.IsGodModeEnabled = false;
        player.IsNoclipPermitted = state.NoclipPermitted;
        player.IsNoclipEnabled = false;
        player.IsMuted = false;

        // Если выход НЕ по волне возрождения — возвращаем строго в Spectator
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

        // Полностью отменяем дефолтную анимацию монетки, чтобы не было задержек и двух кликов
        ev.IsAllowed = false;
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

    public void OnChangedItem(ChangedItemEventArgs ev) { }

    /// <summary>
    /// Генерация текста для ItemHudPanel (вызывается нативным HUD-циклом без мерцания, как и таймер раунда).
    /// </summary>
    private string? GetVanishItemHudText(Player player)
    {
        if (player == null || !IsVanished(player) || player.CurrentItem == null)
            return null;

        if (player.CurrentItem.Type == ItemType.Coin)
        {
            var alive = Player.List
                .Where(p => p != null && p.IsConnected && p.IsAlive && !IsVanished(p) && p.Role.Type != RoleTypeId.Spectator && p.Role.Type != RoleTypeId.None)
                .OrderBy(p => p.Id)
                .ToList();

            if (alive.Count == 0)
            {
                return $"<size=20><color=#ffa94e><b>[ Монетка Наблюдателя ]</b></color></size>\n<size=16><color=#ff4444>Нет живых игроков на сервере</color></size>";
            }

            var state = VanishedStates.GetOrAdd(player.Id, _ => new VanishSavedState());
            int count = alive.Count;
            int currIdx = ((state.TargetPlayerIndex % count) + count) % count;
            int nextIdx = (currIdx + 1) % count;
            int prevIdx = ((currIdx - 1) % count + count) % count;

            var curr = alive[currIdx];
            var next = alive[nextIdx];
            var prev = alive[prevIdx];

            string currHex = ColorUtility.ToHtmlStringRGB(curr.Role.Color);
            string nextHex = ColorUtility.ToHtmlStringRGB(next.Role.Color);
            string prevHex = ColorUtility.ToHtmlStringRGB(prev.Role.Color);

            return $"<size=20><color=#ffa94e><b>[ Монетка Наблюдателя ]</b></color></size>\n" +
                   $"<size=16><color=#a3e635><b>[ЛКМ]</b></color> След: <color=#ffffff>{next.Nickname}</color> <color=#{nextHex}>[{next.Role.Name}]</color>\n" +
                   $"<color=#f87171><b>[ПКМ]</b></color> Пред: <color=#ffffff>{prev.Nickname}</color> <color=#{prevHex}>[{prev.Role.Name}]</color>\n" +
                   $"<color=#c2c2c2>Цель: <color=#{currHex}><b>{curr.Nickname}</b></color> [{curr.Role.Name}] ({currIdx + 1}/{count})</color></size>";
        }

        if (player.CurrentItem.Type == ItemType.KeycardChaosInsurgency)
        {
            return $"<size=20><color=#608f38><b>[ Карта Доступа Хаоса ]</b></color></size>\n" +
                   $"<size=16><color=#cccccc>Осмотр карты: Мини-игра «Змейка»</color></size>";
        }

        return null;
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

    // --- Полный игнор Tesla ворот ---
    public void OnTriggeringTesla(TriggeringTeslaEventArgs ev)
    {
        if (IsVanished(ev.Player))
        {
            ev.IsAllowed = false;
            ev.IsTriggerable = false;
            ev.IsInIdleRange = false;
            ev.IsInHurtingRange = false;
        }
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

    public void OnInteractingEmergencyButton(InteractingEmergencyButtonEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnActivatingWorkstation(ActivatingWorkstationEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnDeactivatingWorkstation(DeactivatingWorkstationEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnActivatingWarheadPanel(ActivatingWarheadPanelEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnChangingLeverStatus(ChangingLeverStatusEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnStartingWarhead(StartingEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnStoppingWarhead(StoppingEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnActivatingScp914(ActivatingEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnChangingKnobSettingScp914(ChangingKnobSettingEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnInteractingScp330(InteractingScp330EventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnInteractingShootingTarget(InteractingShootingTargetEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnActivatingSense(ActivatingSenseEventArgs ev)
    {
        if (IsVanished(ev.Target)) ev.IsAllowed = false;
    }

    public void OnStartingRecall(StartingRecallEventArgs ev)
    {
        if (IsVanished(ev.Target)) ev.IsAllowed = false;
    }

    public void OnEnteringPocketDimension(EnteringPocketDimensionEventArgs ev)
    {
        if (IsVanished(ev.Player)) ev.IsAllowed = false;
    }

    public void OnStrangling(StranglingEventArgs ev)
    {
        if (IsVanished(ev.Target)) ev.IsAllowed = false;
    }

    public void OnDisguising(DisguisingEventArgs ev)
    {
        if (ev.Ragdoll?.Owner != null && IsVanished(ev.Ragdoll.Owner)) ev.IsAllowed = false;
    }
}
