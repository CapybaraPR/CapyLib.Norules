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
                // После детонации боеголовки комплекс заражён — только Поверхность
                if (Warhead.IsDetonated)
                {
                    player.Position = SurfaceTowerPosition;
                    player.ShowZoneHint(HintZone.BottomCenter, "<color=#a3e635><b>Телепортация! 🌀 Но в комплексе уже не выжить...</b></color>", 2.5f, "coin_tp");
                }
                else
                {
                    var validRooms = Room.List.Where(IsValidTeleportRoom).ToList();
                    if (validRooms.Count > 0)
                    {
                        var targetRoom = validRooms[Random.Range(0, validRooms.Count)];
                        player.Position = targetRoom.Position + Vector3.up * 1.2f;
                        player.ShowZoneHint(HintZone.BottomCenter, "<color=#a3e635><b>Успешная телепортация! 🌀</b></color>", 2.5f, "coin_tp");
                    }
                }
            }
            else
            {
                // Неудача (40%): монетка растворяется и исчезает из инвентаря
                var coin = player.CurrentItem?.Type == ItemType.Coin
                    ? player.CurrentItem
                    : player.Items.FirstOrDefault(i => i.Type == ItemType.Coin);

                if (coin != null)
                {
                    player.RemoveItem(coin);
                }

                player.ShowZoneHint(HintZone.BottomCenter, "<color=#ff4444><b>Не твой день 😈 Монетка растворилась...</b></color>", 2.5f, "coin_tp");
            }
        }

        _teleportingPlayers.Remove(player.Id);
    }

    /// <summary>
    /// Проверенная точка спавна на Поверхности (вершина башни, используется и в VanishFeature).
    /// </summary>
    public static readonly Vector3 SurfaceTowerPosition = new(39.2f, 1014.5f, -31.8f);

    /// <summary>
    /// Проверяет, пригодна ли комната для безопасной телепортации монетки.
    /// Исключает гейты, теслы, тупиковые комнаты Офисной зоны (EZ), карманку и Поверхность.
    /// </summary>
    public static bool IsValidTeleportRoom(Room room)
    {
        if (room == null) return false;

        // 1. Исключаем карманку и неизвестные
        if (room.Type is RoomType.Pocket or RoomType.Unknown)
            return false;

        // 2. Исключаем Ворота (Gate A, Gate B)
        if (room.Type is RoomType.EzGateA or RoomType.EzGateB)
            return false;

        // 3. Исключаем Теслы
        if (room.Type is RoomType.HczTesla)
            return false;

        // 4. Исключаем все тупиковые комнаты офисной зоны (Entrance Zone)
        if (room.Type is RoomType.EzVent
                     or RoomType.EzCollapsedTunnel
                     or RoomType.EzConference
                     or RoomType.EzChef
                     or RoomType.EzSmallrooms
                     or RoomType.EzShelter
                     or RoomType.EzIntercom)
        {
            return false;
        }

        // Дополнительная проверка формы Endroom в офисной зоне
        try
        {
            if (room.Zone == ZoneType.Entrance && room.Identifier != null && room.Identifier.Shape == MapGeneration.RoomShape.Endroom)
                return false;
        }
        catch { }

        // 5. Проверяем расстояние до любых тесла-ворот на карте
        try
        {
            if (Exiled.API.Features.TeslaGate.List != null && Exiled.API.Features.TeslaGate.List.Any(t => t != null && (t.Room == room || Vector3.Distance(t.Position, room.Position) < 20f)))
                return false;
        }
        catch { }

        // 6. Исключаем тестовые комнаты и камеру содержания SCP-120 (GlassBox)
        if (room.Type is RoomType.LczGlassBox or RoomType.Hcz939)
            return false;

        // 7. Дополнительная фильтрация по названию (гейты, теслы, теструмы, Поверхность)
        string name = room.Name ?? string.Empty;
        if (name.IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("tesla", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("gr18", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("outside", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("surface", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return true;
    }
}
