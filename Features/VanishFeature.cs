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
/// ����������� ��������� ������ �� ����� � ����� ���������� ����������� (Vanish).
/// </summary>
public sealed class VanishSavedState
{
    public Vector3 Position { get; set; }
    public bool NoclipPermitted { get; set; }
    public int TargetPlayerIndex { get; set; } = 0;

    // ����������� ��������� �������� � ����������������� ��� ������ �� vanish,
    // ����� ����� �� ��� ����� � ���� ��������� ��� ��� ��������� godmode
    public bool WasMuted { get; set; }
    public bool WasGodMode { get; set; }
}

/// <summary>
/// ����� ���������� ����������� (Vanish):
/// 1. ���� �������� ������ �� ���� Spectator (������� � Spectator).
/// 2. ����� � ����� �� ����������� (Surface Tower) � ���� Tutorial.
/// 3. ������������� ������� (��� - ����., ��� - ����.) � ����� ����� (������).
/// 4. ������������ HUD ������������ ��� �������� HP (� ����� ����� ��� ��������).
/// 5. ������ ���������� (���� ����� ��������), ������� � ���������� �������� (VanishIsolationHandler).
/// </summary>
public sealed class VanishFeature
{
    private static readonly ConcurrentDictionary<int, VanishSavedState> VanishedStates = new();
    private static readonly Vector3 TowerSpawnPosition = new(39.2f, 1014.5f, -31.8f);

    public static bool IsVanished(Player? player) => player != null && VanishedStates.ContainsKey(player.Id);

    /// <summary>
    /// ����������� ��������� vanish-������ (��� ��������/��������������).
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
            response = "����� �� ���������.";
            return false;
        }

        if (IsVanished(player))
        {
            DisableVanish(player);
            response = $"<color=yellow>[VANISH]</color> ����� ���������� ����������� ��� <b>{player.Nickname}</b> �������� (������� � Spectator).";
            return true;
        }
        else
        {
            // ��������� ������� ������ �� ���� Spectator!
            if (player.Role.Type != RoleTypeId.Spectator)
            {
                response = "<color=red>[������]</color> ����� ���������� ����������� (Vanish) ����� �������� <b>������ �������� � ������������ (Spectator)</b>.";
                return false;
            }

            EnableVanish(player);
            response = $"<color=green>[VANISH]</color> ����� ���������� ����������� ��� <b>{player.Nickname}</b> ������� �������.";
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

        // 1. ��������� � ���� Tutorial � ������� � ����� �� �����������
        player.Role.Set(RoleTypeId.Tutorial);
        player.Position = TowerSpawnPosition;

        // 2. ������ ������� ��������� � ����� ����� (��� ���� � ������)
        player.ClearInventory();
        player.AddItem(ItemType.Coin);
        player.AddItem(ItemType.KeycardChaosInsurgency);

        // 3. ������ ���������� (���� ��������), ������� � ������� ��������
        VanishIsolationHandler.ApplyIsolation(player);

        // 4. ���������� ������ ������
        player.ShowZoneHint(
            HintZone.TopCenter,
            "<color=#38bdf8><b>?? [��������� �����������] �������</b></color>\n<color=#c2c2c2>�����: <color=#ffa94e>�����</color> � ?? ���/���: <color=#a3e635>�������� � �������</color> � ? �����: <color=#a3e635>����������</color></color>",
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

        // ���� ����� �� �� ����� ����������� � ���������� ������ � Spectator
        if (!respawnWave)
        {
            player.Role.Set(RoleTypeId.Spectator);
            player.ShowZoneHint(
                HintZone.TopCenter,
                "<color=#ff4444><b>?? [��������� �����������] ��������</b></color>",
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

        // ��������� �������� ��������� �������� �������
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
    /// ��������� ������ ��� ItemHudPanel (���������� �������� HUD-������ ��� ��������).
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
                return $"<size=20><color=#ffa94e><b>[ ������� ����������� ]</b></color></size>\n<size=16><color=#ff4444>��� ����� ������� �� �������</color></size>";
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

            return $"<size=20><color=#ffa94e><b>[ ������� ����������� ]</b></color></size>\n" +
                   $"<size=16><color=#a3e635><b>[���]</b></color> ����: <color=#ffffff>{next.Nickname}</color> <color=#{nextHex}>[{next.Role.Name}]</color>\n" +
                   $"<color=#f87171><b>[���]</b></color> ����: <color=#ffffff>{prev.Nickname}</color> <color=#{prevHex}>[{prev.Role.Name}]</color>\n" +
                   $"<color=#c2c2c2>����: <color=#{currHex}><b>{curr.Nickname}</b></color> [{curr.Role.Name}] ({currIdx + 1}/{count})</color></size>";
        }

        if (player.CurrentItem.Type == ItemType.KeycardChaosInsurgency)
        {
            return $"<size=20><color=#608f38><b>[ ����� ������� ����� ]</b></color></size>\n" +
                   $"<size=16><color=#cccccc>������ �����: ����-���� �������</color></size>";
        }

        return null;
    }

    /// <summary>
    /// ������������� ���������� ����������� � ���������� (direction=1) ��� ����������� (direction=-1) ������ ������.
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
            player.ShowZoneHint(HintZone.Notification, "<color=#ff4444><b>��� ����� ������� ��� ����������.</b></color>", 2.0f, "spectator_tp", 20);
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
                    // Выводим из vanish немедленно и возвращаем в Spectator,
                    // чтобы волна заспавнила игрока без окна «голого» Tutorial
                    if (VanishedStates.TryRemove(player.Id, out var state))
                    {
                        VanishIsolationHandler.RemoveIsolation(player);
                        player.ClearInventory();
                        player.IsNoclipPermitted = state.NoclipPermitted;
                        player.Role.Set(RoleTypeId.Spectator);
                        player.ShowZoneHint(
                            HintZone.TopCenter,
                            "<color=#a3e635><b>🚁 [Vanish] Вас забирает волна возрождения!</b></color>",
                            3.5f,
                            "vanish_hud",
                            22
                        );
                    }

                    ev.Players.Add(player);
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
