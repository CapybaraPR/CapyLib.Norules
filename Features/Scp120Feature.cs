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
using Exiled.API.Features.Doors;
using Exiled.API.Features.Pickups;
using Exiled.Events.EventArgs.Player;
using MEC;
using UnityEngine;

namespace Capy.NoRules.Features;

/// <summary>
/// Реализация аномального объекта SCP-120 («Детский бассейн-телепорт»).
/// Обеспечивает безопасную телепортацию игроков (по пулу комнат монетки)
/// и сбалансированную переработку/улучшение предметов без спама мощными пушками.
/// </summary>
public sealed class Scp120Feature : IDisposable
{
    private readonly Scp120Config _config;
    private CoroutineHandle _scanLoop;
    private readonly HashSet<ushort> _activePickupSerials = new();
    private readonly Dictionary<int, DateTime> _playerTeleportCooldowns = new();

    // 1. Обычные предметы: расходники, свет, связь, базовые карточки
    public List<ItemType> CommonItems { get; set; } = new()
    {
        ItemType.Coin, ItemType.KeycardJanitor, ItemType.Radio, ItemType.Lantern,
        ItemType.Medkit, ItemType.Painkillers, ItemType.Flashlight, ItemType.GrenadeFlash
    };

    // 2. Необычные предметы: карты доступа LCZ, медицина, легкая броня, базовый пистолет
    public List<ItemType> UncommonItems { get; set; } = new()
    {
        ItemType.KeycardScientist, ItemType.KeycardResearchCoordinator, ItemType.KeycardZoneManager,
        ItemType.Adrenaline, ItemType.ArmorLight, ItemType.SCP2176, ItemType.GunCOM15
    };

    // 3. Хорошие предметы: карты охраны, боевая броня, полезные SCP-расходники, пистолеты-пулемёты
    public List<ItemType> GoodItems { get; set; } = new()
    {
        ItemType.KeycardGuard, ItemType.KeycardContainmentEngineer,
        ItemType.ArmorCombat, ItemType.SCP207, ItemType.SCP1853, ItemType.GrenadeHE,
        ItemType.GunCOM18, ItemType.GunFSP9
    };

    // 4. Редкие предметы: карты МОГ, тяжелая броня, мощные SCP-артефакты, винтовки
    public List<ItemType> RareItems { get; set; } = new()
    {
        ItemType.KeycardMTFPrivate, ItemType.KeycardMTFOperative, ItemType.ArmorHeavy,
        ItemType.GunCrossvec, ItemType.GunRevolver, ItemType.GunShotgun,
        ItemType.AntiSCP207, ItemType.SCP1576, ItemType.SCP018
    };

    // 5. Легендарные / Очень редкие предметы: карты высшего допуска, легендарные SCP, тяжелое оружие
    public List<ItemType> VeryRareItems { get; set; } = new()
    {
        ItemType.KeycardFacilityManager, ItemType.KeycardMTFCaptain, ItemType.KeycardChaosInsurgency,
        ItemType.SCP500, ItemType.SCP268, ItemType.SCP1344,
        ItemType.GunE11SR, ItemType.GunAK, ItemType.GunLogicer
    };

    public Scp120Feature(Scp120Config config)
    {
        _config = config;
    }

    public void OnRoundStarted()
    {
        if (!_config.IsEnabled) return;

        StopLoop();

        // Приглушаем свет и закрываем двери в комнате GR-18 (LczGlassBox)
        foreach (var room in Room.List)
        {
            if (room.Type == RoomType.LczGlassBox)
            {
                room.Color = new Color32(25, 25, 30, 255);
            }
        }

        foreach (var door in Door.List)
        {
            if (door.Room?.Type == RoomType.LczGlassBox)
            {
                door.IsOpen = false;
            }
        }

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

            // 1. Проверка телепортации игроков при входе в чашу бассейна
            foreach (Player player in Player.List)
            {
                if (player == null || !player.IsAlive) continue;

                Vector2 playerPos2D = new Vector2(player.Position.x, player.Position.z);
                float dist2D = Vector2.Distance(playerPos2D, poolPos2D);
                float yDiff = player.Position.y - poolPos.y;

                if (dist2D <= 1.8f && yDiff >= -1.0f && yDiff <= 2.2f)
                {
                    if (_playerTeleportCooldowns.TryGetValue(player.Id, out var nextUse) && DateTime.UtcNow < nextUse)
                        continue;

                    _playerTeleportCooldowns[player.Id] = DateTime.UtcNow.AddSeconds(_config.TeleportCooldown);
                    TeleportPlayer(player, poolPos);
                }
            }

            // 2. Проверка трансформации брошенных в воду предметов
            foreach (Pickup pickup in Pickup.List.ToList())
            {
                if (pickup == null || pickup.GameObject == null || _activePickupSerials.Contains(pickup.Serial)) continue;

                Vector2 pickupPos2D = new Vector2(pickup.Position.x, pickup.Position.z);
                float dist2D = Vector2.Distance(pickupPos2D, poolPos2D);
                float yDiff = pickup.Position.y - poolPos.y;

                if (dist2D <= 1.8f && yDiff >= -1.0f && yDiff <= 1.8f)
                {
                    Timing.RunCoroutine(ProcessItemTransformation(pickup, poolPos.y + 0.20f));
                }
            }
        }
    }

    private void TeleportPlayer(Player player, Vector3 poolPos)
    {
        Map.ExplodeEffect(player.Position, ProjectileType.Flashbang);

        // Используем проверенный безопасный пул комнат от магической монетки (исключая Поверхность, Теслы, Карманку, Гейты)
        var validRooms = Room.List.Where(BetterCoinsFeature.IsValidTeleportRoom).ToList();
        if (validRooms.Count > 0)
        {
            var targetRoom = validRooms[UnityEngine.Random.Range(0, validRooms.Count)];
            player.Position = targetRoom.Position + Vector3.up * 1.2f;
        }
        else
        {
            player.Position = poolPos + Vector3.up * 3.5f;
        }

        Map.ExplodeEffect(player.Position, ProjectileType.Flashbang);
        player.ShowZoneHint(HintZone.Notification, "<color=#00f5d4>🌀 <b>SCP-120: Телепортация завершена!</b></color>", 2.5f, "scp120_tp", 20);
    }

    private IEnumerator<float> ProcessItemTransformation(Pickup pickup, float targetY)
    {
        _activePickupSerials.Add(pickup.Serial);
        ItemType droppedType = pickup.Type;

        DisablePhysics(pickup);

        Vector3 startPos = pickup.Position;
        float elapsed = 0f;
        const float sinkDuration = 1.1f;

        while (elapsed < sinkDuration && pickup != null && pickup.GameObject != null)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / sinkDuration);
            pickup.Position = Vector3.Lerp(startPos, new Vector3(startPos.x, targetY, startPos.z), t);

            elapsed += 0.05f;
            yield return Timing.WaitForSeconds(0.05f);
        }

        if (pickup != null && pickup.GameObject != null)
        {
            Vector3 finalPos = pickup.Position;
            pickup.Destroy();

            Map.ExplodeEffect(finalPos, ProjectileType.Flashbang);

            ItemType upgradedItem = CalculateUpgradedItem(droppedType);

            yield return Timing.WaitForSeconds(0.08f);

            var newPickup = Pickup.Create(upgradedItem);
            if (newPickup != null)
            {
                newPickup.Position = finalPos + Vector3.up * 0.45f;
                newPickup.Spawn();
                DisablePhysics(newPickup);
                _activePickupSerials.Add(newPickup.Serial);
            }
        }
    }

    private static void DisablePhysics(Pickup? pickup)
    {
        if (pickup == null || pickup.GameObject == null) return;
        try
        {
            foreach (var rb in pickup.GameObject.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.detectCollisions = true;
            }
        }
        catch { }
    }

    private ItemType CalculateUpgradedItem(ItemType inputType)
    {
        int roll = UnityEngine.Random.Range(1, 101);

        // 1. Обычные предметы (монетка, фонарик, рация, карточка уборщика, аптечка, обезболы)
        if (CommonItems.Contains(inputType))
        {
            if (roll <= 45) return CommonItems[UnityEngine.Random.Range(0, CommonItems.Count)];     // 45% Реролл в полезную утилиту/аптечку
            if (roll <= 85) return UncommonItems[UnityEngine.Random.Range(0, UncommonItems.Count)]; // 40% Необычный (Учёный, Адреналин, Броня, COM-15)
            return GoodItems[UnityEngine.Random.Range(0, GoodItems.Count)];                         // 15% Хороший (Охранник, Combat Armor, SCP-207)
        }

        // 2. Необычные предметы (Учёный, COM-15, Адреналин, Легкая броня, SCP-2176)
        if (UncommonItems.Contains(inputType))
        {
            if (roll <= 30) return UncommonItems[UnityEngine.Random.Range(0, UncommonItems.Count)]; // 30% Необычный
            if (roll <= 80) return GoodItems[UnityEngine.Random.Range(0, GoodItems.Count)];         // 50% Хороший (Охранник, Combat Armor, SCP-207, FSP-9)
            return RareItems[UnityEngine.Random.Range(0, RareItems.Count)];                         // 20% Редкий (МОГ Сержант, Heavy Armor, Crossvec)
        }

        // 3. Хорошие предметы (Охранник, Боевая броня, FSP-9, Crossvec, Граната, SCP-207)
        if (GoodItems.Contains(inputType))
        {
            if (roll <= 25) return GoodItems[UnityEngine.Random.Range(0, GoodItems.Count)];         // 25% Хороший
            if (roll <= 80) return RareItems[UnityEngine.Random.Range(0, RareItems.Count)];         // 55% Редкий (МОГ Сержант, Heavy Armor, Crossvec, Shotgun, SCP-018)
            return VeryRareItems[UnityEngine.Random.Range(0, VeryRareItems.Count)];                 // 20% Очень Редкий (Менеджер, SCP-500, E-11, AK)
        }

        // 4. Редкие предметы (МОГ Сержант, Тяжелая броня, Crossvec, Дробовик, SCP-018, SCP-1576)
        if (RareItems.Contains(inputType))
        {
            if (roll <= 35) return RareItems[UnityEngine.Random.Range(0, RareItems.Count)];         // 35% Редкий
            return VeryRareItems[UnityEngine.Random.Range(0, VeryRareItems.Count)];                 // 65% Очень Редкий (Менеджер, Капитан, SCP-500, SCP-268, AK, E-11)
        }

        // 5. Легендарные / Очень редкие предметы (Менеджер, SCP-500, SCP-268, SCP-1344, Logicer, E-11)
        if (VeryRareItems.Contains(inputType))
        {
            return VeryRareItems[UnityEngine.Random.Range(0, VeryRareItems.Count)];
        }

        return UncommonItems[UnityEngine.Random.Range(0, UncommonItems.Count)];
    }

    public void Dispose()
    {
        StopLoop();
    }
}
