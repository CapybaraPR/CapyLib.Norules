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
/// Магическая монетка: телепортация в случайную комнату при подбрасывании.
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
        if (!_config.IsEnabled || ev.Player == null || !ev.Player.IsAlive || ev.Player.IsScp)
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
                var validRooms = Room.List.Where(r => r.Type != RoomType.Pocket && r.Type != RoomType.Unknown).ToList();
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
}
