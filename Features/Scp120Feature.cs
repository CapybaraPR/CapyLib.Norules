using System;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.Studio.Core;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Pickups;
using Exiled.Events.EventArgs.Player;
using MEC;
using UnityEngine;

namespace Capy.NoRules.Features;

/// <summary>
/// Реализация аномального объекта SCP-120 («Детский бассейн-телепорт»).
/// Обеспечивает телепортацию живых игроков и аномальную трансформацию/улучшение погруженных предметов.
/// </summary>
public sealed class Scp120Feature : IDisposable
{
    private readonly Scp120Config _config;
    private CoroutineHandle _scanLoop;
    private readonly HashSet<ushort> _activePickupSerials = new();
    private readonly Dictionary<int, DateTime> _playerTeleportCooldowns = new();

    public List<ItemType> CommonItems { get; set; } = new()
    {
        ItemType.Coin, ItemType.KeycardJanitor, ItemType.Radio, ItemType.Lantern,
        ItemType.Medkit, ItemType.Painkillers, ItemType.Flashlight, ItemType.GrenadeFlash
    };

    public List<ItemType> UncommonItems { get; set; } = new()
    {
        ItemType.KeycardScientist, ItemType.KeycardResearchCoordinator, ItemType.KeycardZoneManager,
        ItemType.Adrenaline, ItemType.ArmorLight, ItemType.GunCOM15, ItemType.GunCOM18,
        ItemType.SCP207, ItemType.SCP1853, ItemType.SCP2176
    };

    public List<ItemType> GoodItems { get; set; } = new()
    {
        ItemType.KeycardGuard, ItemType.KeycardContainmentEngineer,
        ItemType.SCP500, ItemType.ArmorCombat, ItemType.GunFSP9, ItemType.GunCrossvec, ItemType.GrenadeHE
    };

    public List<ItemType> RareItems { get; set; } = new()
    {
        ItemType.KeycardMTFPrivate, ItemType.KeycardMTFOperative, ItemType.ArmorHeavy,
        ItemType.GunRevolver, ItemType.GunShotgun, ItemType.GunCom45, ItemType.GunA7,
        ItemType.GunAK, ItemType.GunE11SR, ItemType.AntiSCP207,
        ItemType.SCP1576, ItemType.SCP018
    };

    public List<ItemType> VeryRareItems { get; set; } = new()
    {
        ItemType.KeycardFacilityManager, ItemType.KeycardO5, ItemType.KeycardMTFCaptain,
        ItemType.KeycardChaosInsurgency, ItemType.ParticleDisruptor, ItemType.MicroHID,
        ItemType.Jailbird, ItemType.GunFRMG0, ItemType.GunLogicer, ItemType.SCP268, ItemType.SCP1344
    };

    public List<ItemType> AmmoTypes { get; set; } = new()
    {
        ItemType.Ammo12gauge, ItemType.Ammo44cal, ItemType.Ammo556x45, ItemType.Ammo762x39, ItemType.Ammo9x19
    };

    private readonly Dictionary<RoomType, Vector3> _targetRooms = new()
    {
        { RoomType.Lcz914, new Vector3(0f, 1f, 0f) },
        { RoomType.LczCafe, new Vector3(0.5f, 0f, 0f) },
        { RoomType.Hcz049, new Vector3(0f, 2f, -1f) },
        { RoomType.Surface, new Vector3(0f, 1f, 0f) },
        { RoomType.EzIntercom, new Vector3(0f, 1f, 0f) },
        { RoomType.HczNuke, new Vector3(0f, 1f, 0f) }
    };

    public Scp120Feature(Scp120Config config)
    {
        _config = config;
    }

    public void OnRoundStarted()
    {
        if (!_config.IsEnabled) return;

        StopLoop();

        // Авто-спавн в GlassBox если включено
        if (_config.AutoSpawnInGlassBox)
        {
            var glassBox = Room.List.FirstOrDefault(r => r.Type == RoomType.LczGlassBox);
            if (glassBox != null)
            {
                var existing = SchematicLoader.SpawnedSchematics.FirstOrDefault(s => s.Name.Equals(_config.SchematicName, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    Vector3 localPos = new Vector3(_config.OffsetX, _config.OffsetY, _config.OffsetZ);
                    Quaternion localRot = Quaternion.Euler(0f, _config.RotationY, 0f);

                    MapManager.SpawnInRoom(glassBox, _config.SchematicName, localPos, localRot);
                }
            }
        }

        _scanLoop = Timing.RunCoroutine(ScanPoolLoop());
    }

    public void OnPickingUpItem(PickingUpItemEventArgs ev)
    {
        if (ev.Pickup != null)
        {
            _activePickupSerials.Remove(ev.Pickup.Serial);
        }
    }

    private void StopLoop()
    {
        if (_scanLoop.IsRunning)
            Timing.KillCoroutines(_scanLoop);

        _activePickupSerials.Clear();
        _playerTeleportCooldowns.Clear();
    }

    private IEnumerator<float> ScanPoolLoop()
    {
        while (true)
        {
            yield return Timing.WaitForSeconds(0.1f);

            var schematic = SchematicLoader.SpawnedSchematics.FirstOrDefault(s => s.Name.IndexOf(_config.SchematicName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (schematic == null || schematic.IsDestroyed) continue;

            Vector3 poolPos = schematic.Position;
            Vector2 poolPos2D = new Vector2(poolPos.x, poolPos.z);

            // 1. Проверка телепортации игроков
            foreach (Player player in Player.List)
            {
                if (player == null || !player.IsAlive) continue;

                Vector2 playerPos2D = new Vector2(player.Position.x, player.Position.z);
                if (Vector2.Distance(playerPos2D, poolPos2D) < _config.PoolRadius && Math.Abs(player.Position.y - poolPos.y) < 1.0f)
                {
                    if (_playerTeleportCooldowns.TryGetValue(player.Id, out var nextUse) && DateTime.UtcNow < nextUse)
                        continue;

                    _playerTeleportCooldowns[player.Id] = DateTime.UtcNow.AddSeconds(_config.TeleportCooldown);
                    TeleportPlayer(player, poolPos);
                }
            }

            // 2. Проверка трансформации предметов
            foreach (Pickup pickup in Pickup.List.ToList())
            {
                if (pickup == null || pickup.GameObject == null || _activePickupSerials.Contains(pickup.Serial)) continue;

                Vector2 pickupPos2D = new Vector2(pickup.Position.x, pickup.Position.z);
                if (Vector2.Distance(pickupPos2D, poolPos2D) < _config.PoolRadius && Math.Abs(pickup.Position.y - poolPos.y) < 1.2f)
                {
                    Timing.RunCoroutine(ProcessItemTransformation(pickup, poolPos.y + 0.35f));
                }
            }
        }
    }

    private void TeleportPlayer(Player player, Vector3 poolPos)
    {
        Map.ExplodeEffect(player.Position, ProjectileType.Flashbang);

        var randomEntry = _targetRooms.ElementAt(UnityEngine.Random.Range(0, _targetRooms.Count));
        Room? targetRoom = Room.List.FirstOrDefault(r => r.Type == randomEntry.Key);

        Vector3 targetPos = targetRoom != null ? targetRoom.Position + randomEntry.Value : poolPos + Vector3.up * 5f;

        player.Position = targetPos;
        Map.ExplodeEffect(player.Position, ProjectileType.Flashbang);

        player.ShowZoneHint(HintZone.Notification, "<color=#00f5d4>🌀 <b>SCP-120: Телепортация завершена!</b></color>", 2.5f, "scp120_tp", 20);
    }

    private IEnumerator<float> ProcessItemTransformation(Pickup pickup, float targetY)
    {
        _activePickupSerials.Add(pickup.Serial);
        ItemType droppedType = pickup.Type;

        Vector3 startPos = pickup.Position;
        float elapsed = 0f;
        const float sinkDuration = 1.4f;

        while (elapsed < sinkDuration && pickup != null && pickup.GameObject != null)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / sinkDuration);
            float wave = Mathf.Sin(elapsed * 8f) * 0.04f * (1f - t);
            pickup.Position = Vector3.Lerp(startPos, new Vector3(startPos.x, targetY, startPos.z), t) + new Vector3(wave, 0, wave);

            elapsed += Timing.DeltaTime;
            yield return Timing.WaitForOneFrame;
        }

        if (pickup != null && pickup.GameObject != null)
        {
            Vector3 finalPos = pickup.Position;
            pickup.Destroy();

            Map.ExplodeEffect(finalPos, ProjectileType.Flashbang);

            int rarity = GetItemRarityValue(droppedType);
            ItemType upgradedItem = CalculateUpgradedItem(rarity);

            yield return Timing.WaitForSeconds(0.1f);

            var newPickup = Pickup.Create(upgradedItem);
            if (newPickup != null)
            {
                newPickup.Position = finalPos + Vector3.up * 0.25f;
                newPickup.Spawn();
                _activePickupSerials.Add(newPickup.Serial);

                Timing.RunCoroutine(FloatingItemAnimation(newPickup, newPickup.Position));
            }
        }
    }

    private IEnumerator<float> FloatingItemAnimation(Pickup pickup, Vector3 origin)
    {
        float elapsed = 0f;
        while (pickup != null && pickup.GameObject != null && _activePickupSerials.Contains(pickup.Serial))
        {
            float bob = Mathf.Sin(elapsed * 2.5f) * 0.08f;
            pickup.Position = origin + new Vector3(0, bob, 0);
            elapsed += Timing.DeltaTime;
            yield return Timing.WaitForOneFrame;
        }
    }

    private int GetItemRarityValue(ItemType type)
    {
        if (CommonItems.Contains(type)) return 10;
        if (UncommonItems.Contains(type)) return 35;
        if (GoodItems.Contains(type)) return 60;
        if (RareItems.Contains(type)) return 85;
        if (VeryRareItems.Contains(type)) return 100;
        return 5;
    }

    private ItemType CalculateUpgradedItem(int currentRarity)
    {
        int roll = UnityEngine.Random.Range(1, 101);

        if (roll <= 10)
            return AmmoTypes[UnityEngine.Random.Range(0, AmmoTypes.Count)];

        int combined = (int)(currentRarity * 0.4f + roll * 0.6f);

        if (combined > 85 && VeryRareItems.Count > 0)
            return VeryRareItems[UnityEngine.Random.Range(0, VeryRareItems.Count)];

        if (combined > 60 && RareItems.Count > 0)
            return RareItems[UnityEngine.Random.Range(0, RareItems.Count)];

        if (combined > 35 && GoodItems.Count > 0)
            return GoodItems[UnityEngine.Random.Range(0, GoodItems.Count)];

        if (combined > 15 && UncommonItems.Count > 0)
            return UncommonItems[UnityEngine.Random.Range(0, UncommonItems.Count)];

        return CommonItems[UnityEngine.Random.Range(0, CommonItems.Count)];
    }

    public void Dispose()
    {
        StopLoop();
    }
}
