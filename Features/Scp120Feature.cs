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
/// - Постепенное потемнение экрана (эффект карманного измерения / 106) с задержкой 2.4с перед телепортацией.
/// - Замедление во время погружения; игрок, вышедший из зоны бассейна до конца анимации, остаётся на месте.
/// - Очередь и задержка между телепортацией игроков / трансформацией предметов из конфига.
/// - Шанс телепорта на Поверхность (конфиг); после детонации боеголовки — только Поверхность.
/// - Анти-фарм: легендарные предметы понижаются при сливе, неизвестные — превращаются в мусор.
/// - Автозакрытие дверей GR-18 через N секунд после старта раунда.
/// </summary>
public sealed class Scp120Feature : IDisposable
{
    private readonly Scp120Config _config;
    private CoroutineHandle _scanLoop;
    private CoroutineHandle _doorCloseLoop;
    private readonly HashSet<ushort> _activePickupSerials = new();
    private readonly HashSet<int> _teleportingPlayerIds = new();

    private DateTime _nextPlayerAllowedTeleportTime = DateTime.MinValue;
    private DateTime _nextItemAllowedTransformTime = DateTime.MinValue;
    private bool _isItemTransforming = false;

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

        // Приглушаем свет в комнате GR-18 (LczGlassBox)
        foreach (var room in Room.List)
        {
            if (room.Type == RoomType.LczGlassBox)
            {
                room.Color = new Color32(25, 25, 30, 255);
            }
        }

        // Автозакрытие всех дверей комнаты через задержку из конфига
        _doorCloseLoop = Timing.RunCoroutine(DoorCloseCoroutine());

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

    private IEnumerator<float> DoorCloseCoroutine()
    {
        yield return Timing.WaitForSeconds(Mathf.Max(0f, _config.DoorsCloseDelaySeconds));

        foreach (var door in Door.List)
        {
            if (door.Room?.Type == RoomType.LczGlassBox)
            {
                door.IsOpen = false;
            }
        }
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

        if (_doorCloseLoop.IsRunning)
            Timing.KillCoroutines(_doorCloseLoop);

        _activePickupSerials.Clear();
        _teleportingPlayerIds.Clear();
        _isItemTransforming = false;
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

            // 1. Проверка телепортации игроков (по одному с кд ~1 сек между игроками)
            if (_teleportingPlayerIds.Count == 0 && DateTime.UtcNow >= _nextPlayerAllowedTeleportTime)
            {
                foreach (Player player in Player.List)
                {
                    if (player == null || !player.IsAlive || _teleportingPlayerIds.Contains(player.Id)) continue;

                    Vector2 playerPos2D = new Vector2(player.Position.x, player.Position.z);
                    float dist2D = Vector2.Distance(playerPos2D, poolPos2D);
                    float yDiff = player.Position.y - poolPos.y;

                    if (dist2D <= _config.PlayerDetectRadius && yDiff >= -1.0f && yDiff <= 2.2f)
                    {
                        Timing.RunCoroutine(TeleportPlayerCoroutine(player, poolPos));
                        break;
                    }
                }
            }

            // 2. Проверка трансформации брошенных в воду предметов (только когда предмет упал в воду)
            if (!_isItemTransforming && DateTime.UtcNow >= _nextItemAllowedTransformTime)
            {
                foreach (Pickup pickup in Pickup.List.ToList())
                {
                    if (pickup == null || pickup.GameObject == null || _activePickupSerials.Contains(pickup.Serial)) continue;

                    Vector2 pickupPos2D = new Vector2(pickup.Position.x, pickup.Position.z);
                    float dist2D = Vector2.Distance(pickupPos2D, poolPos2D);
                    float yDiff = pickup.Position.y - poolPos.y;

                    // Срабатывает только когда предмет действительно попал в воду бассейна
                    if (dist2D <= _config.ItemDetectRadius && yDiff >= -0.4f && yDiff <= 0.65f)
                    {
                        Timing.RunCoroutine(ProcessItemTransformation(pickup));
                        break;
                    }
                }
            }
        }
    }

    private IEnumerator<float> TeleportPlayerCoroutine(Player player, Vector3 poolPos)
    {
        _teleportingPlayerIds.Add(player.Id);

        // Накладываем эффекты затягивания (замедление — игрок может вырваться, покинув зону бассейна)
        try
        {
            player.EnableEffect(EffectType.SinkHole, 3.5f);
            player.EnableEffect(EffectType.Slowness, 255, 3.5f);
            player.EnableEffect(EffectType.Corroding, 3.5f);
            player.EnableEffect(EffectType.Blinded, 3.0f);
        }
        catch { }

        player.ShowZoneHint(HintZone.Notification, "<color=#00f5d4>🌀 <b>SCP-120: Погружение в аномалию.</b></color>", 1.0f, "scp120_tp", 20);
        yield return Timing.WaitForSeconds(0.8f);

        if (!player.IsConnected || !player.IsAlive)
        {
            RemoveEffectsAndForget(player);
            _teleportingPlayerIds.Remove(player.Id);
            yield break;
        }

        player.ShowZoneHint(HintZone.Notification, "<color=#00f5d4>🌀 <b>SCP-120: Погружение в аномалию..</b></color>", 1.0f, "scp120_tp", 20);
        yield return Timing.WaitForSeconds(0.8f);

        if (!player.IsConnected || !player.IsAlive)
        {
            RemoveEffectsAndForget(player);
            _teleportingPlayerIds.Remove(player.Id);
            yield break;
        }

        player.ShowZoneHint(HintZone.Notification, "<color=#00f5d4>🌀 <b>SCP-120: Погружение в аномалию...</b></color>", 1.0f, "scp120_tp", 20);
        yield return Timing.WaitForSeconds(0.8f);

        if (player.IsConnected && player.IsAlive)
        {
            Vector2 poolPos2D = new Vector2(poolPos.x, poolPos.z);
            float dist2D = Vector2.Distance(new Vector2(player.Position.x, player.Position.z), poolPos2D);
            float yDiff = player.Position.y - poolPos.y;
            float cancelRadius = _config.PlayerDetectRadius * Mathf.Max(1f, _config.CancelZoneMultiplier);

            // Игрок успел выйти из зоны во время погружения — аномалия его отпускает
            if (dist2D > cancelRadius || yDiff < -1.5f || yDiff > 2.8f)
            {
                RemoveEffectsAndForget(player);
                player.ShowZoneHint(HintZone.Notification, "<color=#7dd3fc>🌀 Вы вырвались из аномалии.</color>", 2.0f, "scp120_tp", 20);
                _teleportingPlayerIds.Remove(player.Id);
                yield break;
            }

            Map.ExplodeEffect(player.Position, ProjectileType.Flashbang);

            try
            {
                player.DisableEffect(EffectType.Corroding);
                player.DisableEffect(EffectType.SinkHole);
                player.DisableEffect(EffectType.Slowness);
                player.DisableEffect(EffectType.Blinded);
            }
            catch { }

            // После детонации боеголовки комплекс заражён — только Поверхность
            bool forceSurface = Warhead.IsDetonated;
            if (!forceSurface)
            {
                int surfaceRoll = UnityEngine.Random.Range(1, 101);
                forceSurface = surfaceRoll <= Mathf.Clamp(_config.SurfaceChancePercent, 0, 100);
            }

            if (forceSurface)
            {
                player.Position = BetterCoinsFeature.SurfaceTowerPosition + Vector3.up * 0.4f;
            }
            else
            {
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
            }

            Map.ExplodeEffect(player.Position, ProjectileType.Flashbang);
            player.ShowZoneHint(HintZone.Notification, "<color=#00f5d4>🌀 <b>SCP-120: Телепортация завершена!</b></color>", 2.5f, "scp120_tp", 20);
        }

        _teleportingPlayerIds.Remove(player.Id);
        _nextPlayerAllowedTeleportTime = DateTime.UtcNow.AddSeconds(Mathf.Max(0f, _config.TeleportCooldown));
    }

    private static void RemoveEffectsAndForget(Player player)
    {
        try
        {
            player.DisableEffect(EffectType.Corroding);
            player.DisableEffect(EffectType.SinkHole);
            player.DisableEffect(EffectType.Slowness);
            player.DisableEffect(EffectType.Blinded);
        }
        catch { }
    }

    private IEnumerator<float> ProcessItemTransformation(Pickup pickup)
    {
        _isItemTransforming = true;
        _activePickupSerials.Add(pickup.Serial);
        ItemType droppedType = pickup.Type;

        DisablePhysics(pickup);
        Vector3 splashPos = pickup.Position;

        // Вспышка и мгновенное растворение брошенного предмета в воде
        Map.ExplodeEffect(splashPos, ProjectileType.Flashbang);
        pickup.Destroy();

        ItemType upgradedItem = CalculateUpgradedItem(droppedType);

        yield return Timing.WaitForSeconds(0.15f);

        // Появление нового предмета из глубины воды и плавное всплытие
        var newPickup = Pickup.Create(upgradedItem);
        if (newPickup != null)
        {
            Vector3 startFloatPos = new Vector3(splashPos.x, splashPos.y - 0.10f, splashPos.z);
            Vector3 targetFloatPos = new Vector3(splashPos.x, splashPos.y + 0.40f, splashPos.z);

            newPickup.Position = startFloatPos;
            newPickup.Spawn();
            DisablePhysics(newPickup);
            _activePickupSerials.Add(newPickup.Serial);

            // Плавное всплытие на поверхность за 0.5 сек
            float elapsed = 0f;
            const float riseDuration = 0.5f;
            while (elapsed < riseDuration && newPickup != null && newPickup.GameObject != null)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / riseDuration);
                newPickup.Position = Vector3.Lerp(startFloatPos, targetFloatPos, t);

                elapsed += 0.05f;
                yield return Timing.WaitForSeconds(0.05f);
            }

            if (newPickup != null && newPickup.GameObject != null)
            {
                newPickup.Position = targetFloatPos;
                DisablePhysics(newPickup);
            }
        }

        _isItemTransforming = false;
        _nextItemAllowedTransformTime = DateTime.UtcNow.AddSeconds(Mathf.Max(0f, _config.TeleportCooldown));
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
            int rerollRoll = UnityEngine.Random.Range(1, 101);
            if (rerollRoll <= Mathf.Clamp(_config.LegendaryRerollChance, 0, 100))
                return VeryRareItems[UnityEngine.Random.Range(0, VeryRareItems.Count)];             // Шанс реролла из конфига (по умолчанию 40%)
            return RareItems[UnityEngine.Random.Range(0, RareItems.Count)];                         // Иначе понижение до Редкого — фарм легендарок невыгоден
        }

        // Неизвестные предметы (не входят ни в один тир) — бассейн не награждает за мусор
        return CommonItems[UnityEngine.Random.Range(0, CommonItems.Count)];
    }

    public void Dispose()
    {
        StopLoop();
    }
}
