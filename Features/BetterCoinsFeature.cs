using System;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Capy.NoRules.Features;

/// <summary>
/// Магическая монетка (BetterCoins):
/// Бесконечная телепортация в случайную безопасную комнату комплекса.
/// Полностью исключены:
/// - Ворота (Gate A, Gate B, Поверхность)
/// - Комнаты с Tesla-воротами (HczTesla и любые комнаты с активными теслами)
/// - Карманное измерение (Pocket)
/// </summary>
public sealed class BetterCoinsFeature
{
    private readonly BetterCoinsConfig _config;
    private readonly HashSet<int> _teleportingPlayers = new();

    public BetterCoinsFeature(BetterCoinsConfig config)
    {
        _config = config;
    }

    public void OnPlayerSpawned(SpawnedEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Player == null || ev.Player.Role.Type != RoleTypeId.ClassD)
            return;

        if (Random.Range(0, 100) < _config.ClassDSpawnWithCoinChance)
        {
            ev.Player.AddItem(ItemType.Coin);
        }
    }

    public void OnFlippingCoin(FlippingCoinEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Player == null || !ev.Player.IsAlive || ev.Player.IsScp || VanishFeature.IsVanished(ev.Player))
            return;

        if (_teleportingPlayers.Contains(ev.Player.Id))
            return;

        Timing.RunCoroutine(TeleportCoroutine(ev.Player));
    }

    private IEnumerator<float> TeleportCoroutine(Player player)
    {
        _teleportingPlayers.Add(player.Id);

        player.ShowZoneHint(HintZone.BottomCenter, "<color=#ffd285><b>Телепортация.</b></color>", 1.0f, "coin_tp");
        yield return Timing.WaitForSeconds(1.0f);

        if (!player.IsConnected || !player.IsAlive)
        {
            _teleportingPlayers.Remove(player.Id);
            yield break;
        }

        player.ShowZoneHint(HintZone.BottomCenter, "<color=#ffd285><b>Телепортация..</b></color>", 1.0f, "coin_tp");
        yield return Timing.WaitForSeconds(1.0f);

        if (!player.IsConnected || !player.IsAlive)
        {
            _teleportingPlayers.Remove(player.Id);
            yield break;
        }

        player.ShowZoneHint(HintZone.BottomCenter, "<color=#ffd285><b>Телепортация...</b></color>", 1.0f, "coin_tp");
        yield return Timing.WaitForSeconds(1.0f);

        if (player.IsConnected && player.IsAlive)
        {
            int roll = Random.Range(0, 100);
            if (roll < _config.TeleportChance)
            {
                var validRooms = Room.List.Where(IsValidTeleportRoom).ToList();
                if (validRooms.Count > 0)
                {
                    var targetRoom = validRooms[Random.Range(0, validRooms.Count)];
                    player.Position = targetRoom.Position + Vector3.up * 1.2f;
                    player.ShowZoneHint(HintZone.BottomCenter, "<color=#a3e635><b>Успешная телепортация! 🌀</b></color>", 2.5f, "coin_tp");
                }
            }
            else
            {
                player.ShowZoneHint(HintZone.BottomCenter, "<color=#ff4444><b>Не твой день 😈</b></color>", 2.5f, "coin_tp");
            }
        }

        _teleportingPlayers.Remove(player.Id);
    }

    /// <summary>
    /// Проверяет, пригодна ли комната для безопасной телепортации монетки.
    /// Исключает гейты, теслы, поверхность и карманку.
    /// </summary>
    private static bool IsValidTeleportRoom(Room room)
    {
        if (room == null) return false;

        // 1. Исключаем карманку, поверхность, неизвестные
        if (room.Type is RoomType.Pocket or RoomType.Unknown or RoomType.Surface)
            return false;

        // 2. Исключаем Ворота (Gate A, Gate B)
        if (room.Type is RoomType.EzGateA or RoomType.EzGateB)
            return false;

        // 3. Исключаем Теслы
        if (room.Type is RoomType.HczTesla)
            return false;

        // 4. Проверяем расстояние до любых тесла-ворот на карте
        try
        {
            if (Exiled.API.Features.TeslaGate.List != null && Exiled.API.Features.TeslaGate.List.Any(t => t != null && (t.Room == room || Vector3.Distance(t.Position, room.Position) < 20f)))
                return false;
        }
        catch { }

        // 5. Дополнительная фильтрация по названию
        string name = room.Name ?? string.Empty;
        if (name.IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("tesla", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("surface", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return true;
    }
}
