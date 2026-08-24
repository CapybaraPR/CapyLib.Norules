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
using Exiled.Events.EventArgs.Player;
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
    public int TargetPlayerIndex { get; set; } = 0;

    // Сохранённое состояние изоляции — восстанавливается при выходе из vanish,
    // чтобы игрок не мог снять с себя админский мут или слетевший godmode
    public bool WasMuted { get; set; }
    public bool WasGodMode { get; set; }
}

/// <summary>
/// Режим свободного наблюдателя (Vanish):
/// 1. Вход разрешен только из роли Spectator (возврат в Spectator).
/// 2. Спавн в башне на Поверхности (Surface Tower) в роли Tutorial.
/// 3. Навигационная монетка (ЛКМ - след., ПКМ - пред.) и карта Хаоса (Змейка).
/// 4. Динамический HUD способностей над полоской HP (в общем цикле без мерцания).
/// 5. Полная физическая (пули летят насквозь), сетевая и логическая изоляция (VanishIsolationHandler).
/// </summary>
public sealed class VanishFeature
{
    private static readonly ConcurrentDictionary<int, VanishSavedState> VanishedStates = new();
    private static readonly Vector3 TowerSpawnPosition = new(39.2f, 1014.5f, -31.8f);

    public static bool IsVanished(Player? player) => player != null && VanishedStates.ContainsKey(player.Id);

    /// <summary>
    /// Сохранённое состояние vanish-игрока (для изоляции/восстановления).
    /// </summary>
    public static VanishSavedState GetSavedState(Player player)
    {
        return VanishedStates.GetOrAdd(player.Id, _ => new VanishSavedState());
    }

    public void Enable()
    {
        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
        ItemHudPanel.ExternalItemHudProvider = GetVanishItemHudText;
        VanishIsolationHandler.Register();
    }

    public void Disable()
    {
        AssKeybinds.OnKeybindPressed -= OnKeybindPressed;
        if (ItemHudPanel.ExternalItemHudProvider == GetVanishItemHudText)
            ItemHudPanel.ExternalItemHudProvider = null;

        VanishIsolationHandler.Unregister();
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
            
            TargetPlayerIndex = 0
        };

        VanishedStates[player.Id] = state;

        // 1. Переводим в роль Tutorial и спавним в башне на Поверхности
        player.Role.Set(RoleTypeId.Tutorial);
        player.Position = TowerSpawnPosition;

        // 2. Выдаем монетку навигации и карту Хаоса (для игры в змейку)
        player.ClearInventory();
        player.AddItem(ItemType.Coin);
        player.AddItem(ItemType.KeycardChaosInsurgency);

        // 3. Полная физическая (пули насквозь), сетевая и игровая изоляция
        VanishIsolationHandler.ApplyIsolation(player);

        // 4. Оповещение вверху экрана
        player.ShowZoneHint(
            HintZone.TopCenter,
            "<color=#38bdf8><b>?? [СВОБОДНЫЙ НАБЛЮДАТЕЛЬ] ВКЛЮЧЕН</b></color>\n<color=#c2c2c2>Спавн: <color=#ffa94e>Башня</color> • ?? ЛКМ/ПКМ: <color=#a3e635>Телепорт к игрокам</color> • ? Тесла: <color=#a3e635>Игнорирует</color></color>",
            6.0f,
            "vanish_hud",
            22
        );
    }

    public void DisableVanish(Player player, bool respawnWave = false)
    {
        if (!VanishedStates.TryRemove(player.Id, out var state))
            return;

        VanishIsolationHandler.RemoveIsolation(player);
        player.ClearInventory();
        player.IsNoclipPermitted = state.NoclipPermitted;

        // Если выход НЕ по волне возрождения — возвращаем строго в Spectator
        if (!respawnWave)
        {
            player.Role.Set(RoleTypeId.Spectator);
            player.ShowZoneHint(
                HintZone.TopCenter,
                "<color=#ff4444><b>?? [СВОБОДНЫЙ НАБЛЮДАТЕЛЬ] ВЫКЛЮЧЕН</b></color>",
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

        // Полностью отменяем дефолтную анимацию монетки
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

    /// <summary>
    /// Генерация текста для ItemHudPanel (вызывается нативным HUD-циклом без мерцания).
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
}
