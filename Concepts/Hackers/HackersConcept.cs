using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Audio;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.Hints.Models;
using Capy.Engine.Hints.Utilities;
using Capy.Engine.ServerSpecific;
using Capy.Engine.Studio.Core;
using Capy.NoRules.Config;
using Capy.NoRules.Spawns;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Doors;
using Exiled.API.Features.Toys;
using PlayerRoles;
using PlayerStatsSystem;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using Exiled.Events.EventArgs.Warhead;
using MEC;
using Mirror;
using UnityEngine;
using Capy.NoRules.Controllers;
using CustomHint = Capy.Engine.Hints.Models.Hint;
using Light = Exiled.API.Features.Toys.Light;
using Random = UnityEngine.Random;

namespace Capy.NoRules.Concepts.Hackers;

/// <summary>
/// Концепт «Хакеры» — полная версия:
/// Группировка Хакеров + Охранников спавнится вместо волны Повстанцев Хаоса.
/// Их миссия — взломать 4 панели в разных концах комплекса, затем комнату управления.
/// После взлома всех 5 точек запускается протокол OMEGA WARHEAD.
///
/// Под-роли:
///   Хакер   — взламывает панели ([E] рядом, не отходя дальше HackRadius)
///   Охранник — защищает Хакера от других игроков во время взлома
///
/// Контр-игра: MTF/выжившие охотятся на хакеров. Убийство хакера во время
/// взлома сбрасывает прогресс панели. Панели можно «пере-взломать».
/// </summary>
public sealed class HackersConcept
{
    private readonly HackersConfig _config;

    // === ПАНЕЛИ (4 комнаты + комната управления) ===
    private sealed class HackPanel
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SchematicName { get; set; } = "HackerPanel";
        public RoomType RoomType { get; set; }
        public Vector3 Offset { get; set; }
        public float RotationY { get; set; }
        public bool Hacked { get; set; }
        public Vector3? WorldPos { get; set; }
        public SchematicObject? Schematic { get; set; }
        public Light? Glow { get; set; }
    }

    private readonly List<HackPanel> _panels = new();
    private HackPanel? _controlRoom;

    // === СОСТОЯНИЕ ===
    private readonly HashSet<string> _squadMembers = new();
    private readonly HashSet<string> _kittedPlayers = new();
    private readonly HashSet<string> _hackers = new();     // под-роль: взломщик
    private readonly HashSet<string> _guards = new();      // под-роль: охранник
    private readonly HashSet<string> _hackersRank = new(); // под-роль назначена
    private readonly Dictionary<string, DateTime> _spawnProtection = new();
    private readonly List<GameObject> _spawnedObjects = new();
    private readonly List<Door> _lockedDoors = new();
    private readonly List<Door> _controlRoomDoors = new();
    private readonly Dictionary<Room, Color32> _originalRoomColors = new();
    private bool _lightsModified;
    private DateTime _roundStartTime = DateTime.UtcNow;

    // === OMEGA & RADIATION ===
    private bool _omegaActive;
    private bool _omegaDetonated;
    private bool _doorHacked;
    private bool _isHackingDoor;
    private CoroutineHandle _activeDoorCoroutine;
    private CoroutineHandle _hudCoroutine;
    private CoroutineHandle _radiationSpreadCoroutine;
    private CoroutineHandle _radiationSicknessCoroutine;
    private CoroutineHandle _hczDeconCoroutine;
    private int _activeHackProgress;
    private int _activeDoorProgress;
    private int _activeControlProgress;
    private readonly Dictionary<int, CustomHint> _hackerHints = new();

    private readonly HashSet<Room> _contaminatedRooms = new();
    private readonly HashSet<string> _radiationSickPlayers = new();
    private readonly Dictionary<string, float> _timeInRadiation = new();
    private readonly Dictionary<string, float> _timeInCleanRoom = new();
    private readonly Dictionary<string, Light> _radiationAuras = new();
    private bool _hczDecontaminated;

    public bool IsOmegaActive => _omegaActive;
    public bool IsOmegaDetonated => _omegaDetonated;
    public bool IsHczDecontaminated => _hczDecontaminated;

    private const string HackTag = "hackers_hack_";
    private const string OmegaTag = "hackers_omega";

    public struct HackerSpawnPoint
    {
        public Vector3 Offset;
        public Vector3 Rotation;

        public HackerSpawnPoint(Vector3 offset, Vector3 rotation)
        {
            Offset = offset;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// Точки спавна группировки Хакеров на Поверхности (Surface).
    /// </summary>
    public static readonly HackerSpawnPoint[] SurfaceSpawnPoints = new[]
    {
        new HackerSpawnPoint(new Vector3(125.36f, -11.21f, 22.50f), new Vector3(356.91f, 178.65f, 0.00f)),
        new HackerSpawnPoint(new Vector3(121.99f, -11.21f, 20.01f), new Vector3(358.81f, 179.15f, 0.00f)),
        new HackerSpawnPoint(new Vector3(123.42f, -11.21f, 20.03f), new Vector3(358.81f, 179.15f, 0.00f)),
        new HackerSpawnPoint(new Vector3(123.39f, -11.21f, 22.43f), new Vector3(358.81f, 179.15f, 0.00f)),
        new HackerSpawnPoint(new Vector3(121.76f, -11.21f, 22.41f), new Vector3(358.81f, 179.15f, 0.00f)),
        new HackerSpawnPoint(new Vector3(124.59f, -11.21f, 19.73f), new Vector3(358.81f, 179.15f, 0.00f)),
        new HackerSpawnPoint(new Vector3(123.73f, -11.21f, 21.41f), new Vector3(358.81f, 179.15f, 0.00f)),
    };

    public static List<int> GetRandomizedSpawnIndices(int count)
    {
        var list = new List<int>();
        var pool = Enumerable.Range(0, SurfaceSpawnPoints.Length).ToList();

        while (list.Count < count)
        {
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            list.AddRange(pool);
        }

        return list.Take(count).ToList();
    }

    public static void TeleportToSurfaceSpawn(Player player, int index = 0, float delay = 0.35f)
    {
        Timing.CallDelayed(delay, () =>
        {
            try
            {
                if (player == null || !player.IsConnected || !player.IsAlive) return;

                var surfaceRoom = Room.List.FirstOrDefault(r => r.Type == RoomType.Surface);
                if (surfaceRoom == null)
                {
                    Log.Warn("[Hackers] Комната Surface не найдена.");
                    return;
                }

                var pt = SurfaceSpawnPoints[Math.Abs(index) % SurfaceSpawnPoints.Length];

                Vector3 targetPos = surfaceRoom.Position + surfaceRoom.Rotation * pt.Offset;
                Quaternion targetRot = surfaceRoom.Rotation * Quaternion.Euler(pt.Rotation);

                player.Position = targetPos;
                player.Rotation = targetRot;

                Log.Debug($"[Hackers] Игрок {player.Nickname} телепортирован на спавн Surface #{index}: {targetPos}");
            }
            catch (Exception ex)
            {
                Log.Error($"[Hackers] Ошибка телепортации игрока на Surface спавн: {ex}");
            }
        });
    }

    public HackersConcept(HackersConfig config)
    {
        _config = config;
    }

    // ------------------------------------------------------------------
    //  Определения панелей
    // ------------------------------------------------------------------

    private void InitPanels()
    {
        _panels.Clear();

        _panels.Add(new HackPanel
        {
            Id = "server",
            DisplayName = "Серверная HCZ",
            SchematicName = "HackerServerPanel",
            RoomType = RoomType.HczServerRoom,
            Offset = new Vector3(6.65f, -4.50f, -4.26f),
            RotationY = 270f
        });
        _panels.Add(new HackPanel
        {
            Id = "hid",
            DisplayName = "Комната MicroHID",
            SchematicName = "HackerHidPanel",
            RoomType = RoomType.HczHid,
            Offset = new Vector3(6.77f, 0.00f, 3.93f),
            RotationY = 270f
        });
        _panels.Add(new HackPanel
        {
            Id = "nuke",
            DisplayName = "Боеголовка",
            RoomType = RoomType.HczNuke,
            Offset = new Vector3(-6.92f, -72.45f, -2.82f),
            RotationY = 0f
        });
        _panels.Add(new HackPanel
        {
            Id = "079",
            DisplayName = "Комната 079",
            SchematicName = "Hacker079Panel",
            RoomType = RoomType.Hcz079,
            Offset = new Vector3(-1.33f, -5.30f, -15.99f),
            RotationY = 0f
        });

        _controlRoom = new HackPanel
        {
            Id = "control",
            DisplayName = "Комната Управления",
            RoomType = RoomType.Hcz049,
            Offset = new Vector3(26.96f, 93.65f, 10.13f),
            RotationY = 270f
        };
    }

    // ------------------------------------------------------------------
    //  Lifecycle
    // ------------------------------------------------------------------

    public void Enable()
    {
        if (!_config.IsEnabled) return;

        Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
        Exiled.Events.Handlers.Server.EndingRound += OnEndingRound;
        Exiled.Events.Handlers.Player.Spawned += OnSpawned;
        Exiled.Events.Handlers.Player.ChangingRole += OnChangingRole;
        Exiled.Events.Handlers.Player.Died += OnDied;
        Exiled.Events.Handlers.Player.Left += OnLeft;
        Exiled.Events.Handlers.Player.Hurting += OnHurting;
        Exiled.Events.Handlers.Player.UsedItem += OnUsedItem;
        Exiled.Events.Handlers.Player.InteractingDoor += OnInteractingDoor;
        Exiled.Events.Handlers.Warhead.Starting += OnWarheadStarting;
        Exiled.Events.Handlers.Player.ActivatingWarheadPanel += OnActivatingWarheadPanel;
        Exiled.Events.Handlers.Warhead.ChangingLeverStatus += OnChangingLeverStatus;
        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
    }

    public void Disable()
    {
        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
        Exiled.Events.Handlers.Server.EndingRound -= OnEndingRound;

        Exiled.Events.Handlers.Player.Spawned -= OnSpawned;
        Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        Exiled.Events.Handlers.Player.Left -= OnLeft;
        Exiled.Events.Handlers.Player.Hurting -= OnHurting;
        Exiled.Events.Handlers.Player.UsedItem -= OnUsedItem;
        Exiled.Events.Handlers.Player.InteractingDoor -= OnInteractingDoor;
        Exiled.Events.Handlers.Warhead.Starting -= OnWarheadStarting;
        Exiled.Events.Handlers.Player.ActivatingWarheadPanel -= OnActivatingWarheadPanel;
        Exiled.Events.Handlers.Warhead.ChangingLeverStatus -= OnChangingLeverStatus;
        AssKeybinds.OnKeybindPressed -= OnKeybindPressed;

        ResetState();
    }

    // ------------------------------------------------------------------
    //  Раунд
    // ------------------------------------------------------------------

    private void OnWaitingForPlayers() => ResetState();

    private void OnRestartingRound()
    {
        Timing.KillCoroutines("hackers_hack");
        Timing.KillCoroutines(OmegaTag);
        Timing.KillCoroutines($"{HackTag}rad_spread");
        Timing.KillCoroutines($"{HackTag}rad_sickness");
        Timing.KillCoroutines($"{HackTag}hcz_decon");

        RestoreLights();
        TeardownVisuals();
        ResetState();
    }

    private void ResetState()
    {
        _activeHackProgress = 0;
        _activeDoorProgress = 0;
        _activeControlProgress = 0;
        if (_hudCoroutine.IsRunning)
            Timing.KillCoroutines(_hudCoroutine);

        ClearAllHackerHints();

        _doorHacked = false;
        _isHackingDoor = false;
        if (_activeDoorCoroutine.IsRunning)
            Timing.KillCoroutines(_activeDoorCoroutine);

        if (_radiationSpreadCoroutine.IsRunning)
            Timing.KillCoroutines(_radiationSpreadCoroutine);
        if (_radiationSicknessCoroutine.IsRunning)
            Timing.KillCoroutines(_radiationSicknessCoroutine);
        if (_hczDeconCoroutine.IsRunning)
            Timing.KillCoroutines(_hczDeconCoroutine);

        UnlockControlRoomDoors();
        TeardownVisuals();
        _panels.Clear();
        _controlRoom = null;
        _squadMembers.Clear();
        _kittedPlayers.Clear();
        _hackers.Clear();
        _guards.Clear();
        _hackersRank.Clear();
        _spawnProtection.Clear();
        _lockedDoors.Clear();
        _controlRoomDoors.Clear();
        _originalRoomColors.Clear();
        _lightsModified = false;
        _omegaActive = false;
        _omegaDetonated = false;
        _hczDecontaminated = false;
        _contaminatedRooms.Clear();
        _radiationSickPlayers.Clear();
        _timeInRadiation.Clear();
        _timeInCleanRoom.Clear();

        foreach (var light in _radiationAuras.Values.ToList())
        {
            try
            {
                light?.Destroy();
            }
            catch { }
        }
        _radiationAuras.Clear();

        try { AudioToggle.DestroyGlobal("omegawarheadmusic"); } catch { }
        foreach (var r in Room.List)
        {
            try { AudioToggle.DestroyForRoom(r); } catch { }
        }
        foreach (var p in Player.List)
        {
            try { AudioToggle.DestroyForPlayer(p); } catch { }
        }

        ConceptsController.Disable();
    }

    private void OnRoundStarted()
    {
        _roundStartTime = DateTime.UtcNow;
        InitPanels();
        BuildAllPanels();
        Timing.CallDelayed(2.0f, FindAndLockControlRoomDoors);
        if (_hudCoroutine.IsRunning)
            Timing.KillCoroutines(_hudCoroutine);
        _hudCoroutine = Timing.RunCoroutine(HackersHudLoop(), "hackers_hud_loop");
    }

    private void FindAndLockControlRoomDoors()
    {
        _controlRoomDoors.Clear();
        var room = Room.List.FirstOrDefault(r => r.Type == RoomType.Hcz049);
        if (room == null || _controlRoom == null) return;

        Vector3 controlPos = room.Position + room.Rotation * _controlRoom.Offset;

        foreach (var door in Door.List)
        {
            if (door == null) continue;

            if (door.IsElevator || door.Type.ToString().Contains("Elevator"))
                continue;

            if (door.Room?.Type == RoomType.Hcz049 || Vector3.Distance(door.Position, controlPos) < 35f)
            {
                _controlRoomDoors.Add(door);
                try
                {
                    door.IsOpen = false;
                    door.ChangeLock(DoorLockType.AdminCommand);
                }
                catch { }
            }
        }

        Log.Info($"[Hackers] Заблокировано дверей Комнаты Управления (173 chamber): {_controlRoomDoors.Count}");
    }

    private void UnlockControlRoomDoors()
    {
        foreach (var door in _controlRoomDoors)
        {
            try
            {
                if (door != null)
                {
                    door.Unlock();
                    door.IsOpen = true;
                }
            }
            catch { }
        }
        _controlRoomDoors.Clear();
    }

    // ------------------------------------------------------------------
    //  Построение панелей (JSON-схематика в каждой комнате)
    // ------------------------------------------------------------------

    private void BuildAllPanels()
    {
        foreach (var panel in _panels)
        {
            try
            {
                var room = Room.List.FirstOrDefault(r => r.Type == panel.RoomType);
                if (room == null)
                {
                    Log.Warn($"[Hackers] Комната {panel.RoomType} не найдена для панели '{panel.DisplayName}'.");
                    continue;
                }

                Vector3 pos = room.Position + room.Rotation * panel.Offset;
                Quaternion rot = room.Rotation * Quaternion.Euler(0f, panel.RotationY, 0f);
                panel.WorldPos = pos;

                var schematic = SchematicLoader.Spawn(!string.IsNullOrEmpty(panel.SchematicName) ? panel.SchematicName : "HackerPanel", pos, rot);
                panel.Schematic = schematic;

                // Индикаторное HDR-свечение
                var glow = Light.Create(
                    position: pos + rot * new Vector3(0f, 1.4f, 0.5f),
                    rotation: null,
                    scale: Vector3.one,
                    spawn: false,
                    color: new Color32(167, 139, 250, 255));

                if (glow != null)
                {
                    glow.Intensity = 2.5f;
                    glow.Range = 3.5f;
                    glow.Spawn();
                    panel.Glow = glow;
                    _spawnedObjects.Add(glow.GameObject);
                }

                StationsManager.Register(
                    $"hackers_{panel.Id}",
                    pos + rot * new Vector3(0f, 0.5f, 0.8f),
                    Mathf.Max(1.5f, _config.PanelRadius),
                    p => StartPanelHack(p, panel));
            }
            catch (Exception ex)
            {
                Log.Error($"[Hackers] Ошибка построения панели '{panel.DisplayName}': {ex}");
            }
        }
    }

    private void BuildControlRoom()
    {
        if (_controlRoom == null) return;

        try
        {
            var room = Room.List.FirstOrDefault(r => r.Type == _controlRoom.RoomType);
            if (room == null) return;

            Vector3 pos = room.Position + room.Rotation * _controlRoom.Offset;
            Quaternion rot = room.Rotation * Quaternion.Euler(0f, _controlRoom.RotationY, 0f);
            _controlRoom.WorldPos = pos;

            // Спавним полноразмерный Командный мостик / Комнату управления
            var schematic = SchematicLoader.Spawn("HackerControlRoom", pos, rot);
            _controlRoom.Schematic = schematic;

            // Спавним тактические припасы на столах
            SpawnControlRoomLoot(pos, rot);

            // Точка взаимодействия за главным терминалом
            StationsManager.Register(
                "hackers_control",
                pos + rot * new Vector3(0f, 0.85f, 0.45f),
                Mathf.Max(1.8f, _config.PanelRadius),
                p => StartControlHack(p));

            // Мощное HDR-свечение Комнаты Управления
            var glow = Light.Create(
                position: pos + rot * new Vector3(0f, 1.6f, 0.4f),
                rotation: null,
                scale: Vector3.one,
                spawn: false,
                color: new Color32(0, 229, 255, 255));
            if (glow != null)
            {
                glow.Intensity = 5.5f;
                glow.Range = 8.5f;
                glow.Spawn();
                _controlRoom.Glow = glow;
                _spawnedObjects.Add(glow.GameObject);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Hackers] Ошибка построения комнаты управления: {ex}");
        }
    }

    private void SpawnControlRoomLoot(Vector3 centerPos, Quaternion rot)
    {
        try
        {
            // Левый оружейный кейс: тяжелая броня и светошумовая граната
            var pickup1 = Exiled.API.Features.Pickups.Pickup.Create(ItemType.ArmorHeavy);
            if (pickup1 != null)
            {
                pickup1.Position = centerPos + rot * new Vector3(-1.7f, 0.95f, 1.5f);
                pickup1.Spawn();
                _spawnedObjects.Add(pickup1.GameObject);
            }

            var pickup2 = Exiled.API.Features.Pickups.Pickup.Create(ItemType.GrenadeFlash);
            if (pickup2 != null)
            {
                pickup2.Position = centerPos + rot * new Vector3(-1.5f, 0.95f, 1.5f);
                pickup2.Spawn();
                _spawnedObjects.Add(pickup2.GameObject);
            }

            // Правый медицинский кейс: аптечка и адреналин
            var pickup3 = Exiled.API.Features.Pickups.Pickup.Create(ItemType.Medkit);
            if (pickup3 != null)
            {
                pickup3.Position = centerPos + rot * new Vector3(1.7f, 0.95f, 1.5f);
                pickup3.Spawn();
                _spawnedObjects.Add(pickup3.GameObject);
            }

            var pickup4 = Exiled.API.Features.Pickups.Pickup.Create(ItemType.Adrenaline);
            if (pickup4 != null)
            {
                pickup4.Position = centerPos + rot * new Vector3(1.5f, 0.95f, 1.5f);
                pickup4.Spawn();
                _spawnedObjects.Add(pickup4.GameObject);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Hackers] Ошибка спавна лута в Комнате Управления: {ex}");
        }
    }

    private void TeardownVisuals()
    {
        foreach (var panel in _panels)
        {
            if (panel.Schematic != null && !panel.Schematic.IsDestroyed)
            {
                SchematicLoader.RemoveInstance(panel.Schematic);
            }
            StationsManager.Unregister($"hackers_{panel.Id}");
        }

        if (_controlRoom?.Schematic != null && !_controlRoom.Schematic.IsDestroyed)
        {
            SchematicLoader.RemoveInstance(_controlRoom.Schematic);
        }
        StationsManager.Unregister("hackers_control");

        foreach (var go in _spawnedObjects)
        {
            try
            {
                if (go == null) continue;
                if (Mirror.NetworkServer.active)
                    Mirror.NetworkServer.UnSpawn(go);
                UnityEngine.Object.Destroy(go);
            }
            catch { }
        }
        _spawnedObjects.Clear();
    }

    /// <summary>
    /// Добавляет игрока в группировку Хакеров (для .gcr).
    /// </summary>
    public void AddMember(Player player) => AddHacker(player);

    public void AddHacker(Player player)
    {
        if (player == null) return;

        bool roleChanged = false;
        if (player.Role.Type != RoleTypeId.ChaosConscript)
        {
            player.Role.Set(RoleTypeId.ChaosConscript, SpawnReason.Respawn, RoleSpawnFlags.All);
            roleChanged = true;
        }

        _squadMembers.Add(player.UserId);
        _hackers.Add(player.UserId);
        _guards.Remove(player.UserId);

        float delay = roleChanged ? 0.35f : 0.05f;

        Timing.CallDelayed(delay, () =>
        {
            try
            {
                if (player == null || !player.IsConnected || !player.IsAlive) return;

                CustomUnits.AssignMember(player, "Группировка «Хакеры»", "#a78bfa", "Хакер");

                player.ClearInventory();
                player.AddItem(ItemType.KeycardChaosInsurgency);
                player.AddItem(ItemType.GunE11SR);
                player.AddItem(ItemType.GunFRMG0);
                player.AddItem(ItemType.SCP500);
                player.AddItem(ItemType.ArmorHeavy);
                player.AddItem(ItemType.Radio);
                player.AddItem(ItemType.Lantern);
                player.AddItem(ItemType.GrenadeFlash);
            }
            catch { }

            _spawnProtection[player.UserId] = DateTime.UtcNow.AddSeconds(10f);
            TeleportToSurfaceSpawn(player, Random.Range(0, SurfaceSpawnPoints.Length), 0.05f);

            player.ShowZoneHint(HintZone.TopCenter,
                "<color=#a78bfa><b>💻 Вы — Хакер</b></color>\n" +
                "<size=70%><color=#c2c2c2>Взломайте 4 панели в HCZ ([E]), затем Комнату Управления (HCZ 049)</color></size>",
                8f, "hackers_brief", 20);
        });
    }

    public void AddGuard(Player player)
    {
        if (player == null) return;

        bool roleChanged = false;
        if (player.Role.Type != RoleTypeId.ChaosRifleman)
        {
            player.Role.Set(RoleTypeId.ChaosRifleman, SpawnReason.Respawn, RoleSpawnFlags.All);
            roleChanged = true;
        }

        _squadMembers.Add(player.UserId);
        _guards.Add(player.UserId);
        _hackers.Remove(player.UserId);

        float delay = roleChanged ? 0.35f : 0.05f;

        Timing.CallDelayed(delay, () =>
        {
            try
            {
                if (player == null || !player.IsConnected || !player.IsAlive) return;

                CustomUnits.AssignMember(player, "Группировка «Хакеры»", "#a78bfa", "Охранник");

                player.ClearInventory();
                player.AddItem(ItemType.KeycardChaosInsurgency);
                player.AddItem(ItemType.GunE11SR);
                player.AddItem(ItemType.GunShotgun);
                player.AddItem(ItemType.SCP500);
                player.AddItem(ItemType.GrenadeHE);
                player.AddItem(ItemType.GrenadeFlash);
                player.AddItem(ItemType.Lantern);
                player.AddItem(ItemType.ArmorHeavy);
            }
            catch { }

            _spawnProtection[player.UserId] = DateTime.UtcNow.AddSeconds(10f);
            TeleportToSurfaceSpawn(player, Random.Range(0, SurfaceSpawnPoints.Length), 0.05f);

            player.ShowZoneHint(HintZone.TopCenter,
                "<color=#ef4444><b>🛡 Вы — Охранник Хакера</b></color>\n" +
                "<size=70%><color=#c2c2c2>Защищайте Хакера от врагов во время взлома панелей!</color></size>",
                8f, "hackers_brief", 20);
        });
    }

    // ------------------------------------------------------------------
    //  Членство
    // ------------------------------------------------------------------

    private bool IsSquadMember(Player? p) => p != null && _squadMembers.Contains(p.UserId);
    private bool IsHacker(Player? p) => p != null && _hackers.Contains(p.UserId);

    private void OnSpawned(SpawnedEventArgs ev)
    {
        if (ev.Player == null || !_squadMembers.Contains(ev.Player.UserId)) return;
        if (_kittedPlayers.Contains(ev.Player.UserId)) return;

        _kittedPlayers.Add(ev.Player.UserId);
    }

    private void OnChangingRole(ChangingRoleEventArgs ev)
    {
        if (ev.Player == null) return;

        CureRadiation(ev.Player);

        if (IsScpRole(ev.NewRole) || ev.NewRole is RoleTypeId.None or RoleTypeId.Spectator or RoleTypeId.Overwatch)
        {
            if (_squadMembers.Remove(ev.Player.UserId))
            {
                bool wasHacker = _hackers.Remove(ev.Player.UserId);
                _guards.Remove(ev.Player.UserId);
                _kittedPlayers.Remove(ev.Player.UserId);
                CustomUnits.RemoveMember(ev.Player);

                if (wasHacker && _hackers.Count == 0)
                    ResetPanelHack("все хакеры выбыли");
            }
        }

        _spawnProtection.Remove(ev.Player.UserId);
    }

    private void OnDied(DiedEventArgs ev)
    {
        if (ev.Player == null) return;

        CureRadiation(ev.Player);

        if (_squadMembers.Remove(ev.Player.UserId))
        {
            bool wasHacker = _hackers.Remove(ev.Player.UserId);
            _guards.Remove(ev.Player.UserId);
            _kittedPlayers.Remove(ev.Player.UserId);
            CustomUnits.RemoveMember(ev.Player);

            if (wasHacker && _hackers.Count == 0)
                ResetPanelHack("все хакеры погибли");
        }

        _spawnProtection.Remove(ev.Player.UserId);
    }

    private void OnLeft(LeftEventArgs ev)
    {
        if (ev.Player == null) return;
        CureRadiation(ev.Player);
        _squadMembers.Remove(ev.Player.UserId);
        _hackers.Remove(ev.Player.UserId);
        _guards.Remove(ev.Player.UserId);
        _kittedPlayers.Remove(ev.Player.UserId);
        _spawnProtection.Remove(ev.Player.UserId);
    }

    private void OnHurting(HurtingEventArgs ev)
    {
        if (ev.Player == null) return;

        // 1. Защита при спавне (10 сек)
        if (_spawnProtection.TryGetValue(ev.Player.UserId, out var until) &&
            DateTime.UtcNow < until &&
            ev.DamageHandler.Type != DamageType.Warhead)
        {
            ev.IsAllowed = false;
            return;
        }

        // Повышенный получаемый урон при лучевой болезни (+25%)
        if (_radiationSickPlayers.Contains(ev.Player.UserId) &&
            ev.DamageHandler.Type != DamageType.Custom &&
            ev.DamageHandler.Type != DamageType.Decontamination &&
            ev.DamageHandler.Type != DamageType.Warhead)
        {
            ev.Amount *= _config.RadiationVulnerabilityMultiplier;
        }

        if (ev.Attacker == null || ev.Attacker == ev.Player) return;

        bool isVictimHacker = IsSquadMember(ev.Player);
        bool isAttackerHacker = IsSquadMember(ev.Attacker);

        // 2. Внутри отряда Хакеров — защита от френдли фаера
        if (isVictimHacker && isAttackerHacker)
        {
            ev.IsAllowed = false;
            return;
        }

        // 3. Хакеры враждебны ко всем (включая обычных Повстанцев Хаоса)
        if (isVictimHacker || isAttackerHacker)
        {
            ev.IsAllowed = true;
        }
    }

    private void OnUsedItem(UsedItemEventArgs ev)
    {
        if (ev.Player == null || ev.Item == null) return;

        if (ev.Item.Type == ItemType.SCP500)
        {
            if (_radiationSickPlayers.Contains(ev.Player.UserId))
            {
                CureRadiation(ev.Player);
                ev.Player.ShowZoneHint(HintZone.TopCenter,
                    "<color=#22c55e><b>💊 SCP-500 ПОЛНОСТЬЮ ИЗЛЕЧИЛ ЛУЧЕВУЮ БОЛЕЗНЬ!</b></color>",
                    6f, "rad_500_cure", 22);
            }
        }
    }

    private void OnEndingRound(EndingRoundEventArgs ev)
    {
        if (ev.IsForceEnded) return;

        // Если OMEGA боеголовка сдетонировала — раунд продолжается, пока живы бойцы разных команд
        if (_omegaDetonated)
        {
            var alive = Player.List.Where(p => p is { IsAlive: true }).ToList();
            if (alive.Count > 0)
            {
                bool hasScp = alive.Any(p => p.IsScp);
                bool hasHackers = alive.Any(p => IsSquadMember(p));
                bool hasChaos = alive.Any(p => p.Role.Team == Team.ChaosInsurgency && !IsSquadMember(p));
                bool hasFoundation = alive.Any(p => p.Role.Team is Team.FoundationForces or Team.Scientists);
                bool hasClassD = alive.Any(p => p.Role.Team == Team.ClassD);

                int teamsAlive = (hasScp ? 1 : 0) + (hasHackers ? 1 : 0) + (hasChaos ? 1 : 0) + (hasFoundation ? 1 : 0) + (hasClassD ? 1 : 0);
                if (teamsAlive > 1)
                {
                    ev.IsAllowed = false;
                    return;
                }
            }
        }

        // Проверяем живых участников группировки Хакеров
        int aliveHackers = _squadMembers.Count(id => Player.Get(id) is { IsAlive: true });
        if (aliveHackers == 0) return;

        // Если кроме хакеров есть другие живые игроки (Хаос, МОГ, SCP, Ученые, Д-класс)
        bool otherAlive = Player.List.Any(p => p.IsAlive && !IsSquadMember(p));
        if (otherAlive)
        {
            // Раунд не заканчивается, пока Хакеры не уничтожат всех или сами не погибнут
            ev.IsAllowed = false;
        }
    }

    private void OnWarheadStarting(StartingEventArgs ev)
    {
        if (_omegaActive || _omegaDetonated)
        {
            ev.IsAllowed = false;
            try { Warhead.Stop(); Warhead.IsLocked = true; } catch { }
        }
    }

    private void OnActivatingWarheadPanel(ActivatingWarheadPanelEventArgs ev)
    {
        if (_omegaActive || _omegaDetonated)
        {
            ev.IsAllowed = false;
            ev.Player?.ShowZoneHint(HintZone.BottomCenter,
                "<color=#ef4444><b>[ ОШИБКА ]</b> Альфа-боеголовка заблокирована протоколом OMEGA!</color>",
                2.5f, "alpha_disabled", 22);
        }
    }

    private void OnChangingLeverStatus(ChangingLeverStatusEventArgs ev)
    {
        if (_omegaActive || _omegaDetonated)
        {
            ev.IsAllowed = false;
        }
    }

    private void OnInteractingDoor(InteractingDoorEventArgs ev)
    {
        if (ev.Player == null || ev.Door == null) return;

        // Блокируем двери и лифты после детонации OMEGA
        if (_omegaDetonated && _lockedDoors.Contains(ev.Door))
        {
            ev.IsAllowed = false;
            ev.Player.ShowZoneHint(HintZone.BottomCenter,
                "<color=#ef4444>🔒 Гермоворота заблокированы протоколом изоляции OMEGA!</color>",
                2f, "omega_door_locked", 20);
            return;
        }

        // Блокируем лифты во время отсчёта Omega
        if (_omegaActive && ev.Door.Type is DoorType.ElevatorGateA or DoorType.ElevatorGateB)
        {
            ev.IsAllowed = false;
            return;
        }

        // Блокировка и взлом дверей Комнаты Управления
        if (_controlRoomDoors.Contains(ev.Door))
        {
            // 1. Ещё не все 4 терминала комплекса взломаны
            if (!_panels.All(p => p.Hacked))
            {
                ev.IsAllowed = false;
                int hackedCount = _panels.Count(p => p.Hacked);
                ev.Player.ShowZoneHint(HintZone.Notification,
                    $"<color=#ef4444><b>🔒 [ПРОТОКОЛ ИЗОЛЯЦИИ АКТИВЕН]</b></color>\n" +
                    $"<size=75%><color=#c2c2c2>Комната Управления заблокирована до взлома всех 4 терминалов.\n" +
                    $"Взломано терминалов комплекса: <color=#ffd285><b>{hackedCount}/4</b></color></color></size>",
                    3.5f, "control_locked", 25);
                return;
            }

            // 2. Все 4 терминала взломаны, но дверь ещё не взломана
            if (!_doorHacked)
            {
                ev.IsAllowed = false;

                if (!IsHacker(ev.Player))
                {
                    if (IsSquadMember(ev.Player))
                    {
                        ev.Player.ShowZoneHint(HintZone.Notification,
                            "<color=#facc15>⚠️ Вы — Охранник. Только Хакер может взломать гермодверь Комнаты Управления!</color>",
                            3.5f, "door_guard", 25);
                    }
                    else
                    {
                        ev.Player.ShowZoneHint(HintZone.Notification,
                            "<color=#ef4444>🔒 Гермодверь Комнаты Управления заблокирована электронной системой доступа.</color>",
                            3f, "door_locked", 25);
                    }
                    return;
                }

                if (_isHackingDoor)
                {
                    ev.Player.ShowZoneHint(HintZone.Notification,
                        "<color=#facc15>Уже идёт взлом гермодвери Комнаты Управления!</color>",
                        2.5f, "door_hack_progress", 25);
                    return;
                }

                StartDoorHack(ev.Player, ev.Door);
            }
        }
    }

    private static bool IsScpRole(RoleTypeId role)
        => role is RoleTypeId.Scp049 or RoleTypeId.Scp0492 or RoleTypeId.Scp079 or RoleTypeId.Scp096
            or RoleTypeId.Scp106 or RoleTypeId.Scp173 or RoleTypeId.Scp3114 or RoleTypeId.Scp939;

    // ------------------------------------------------------------------
    //  Взлом панелей
    // ------------------------------------------------------------------

    private void OnKeybindPressed(Player player, CustomKeybind keybind)
    {
        if (keybind != CustomKeybind.E)
            return;

        StationsManager.TryInvokeNearest(player);
    }

    private CoroutineHandle _activeHackCoroutine;
    private CoroutineHandle _activeControlCoroutine;
    private string? _activeHackPanelId;
    
    private void StartPanelHack(Player player, HackPanel panel)
    {
        if (panel.WorldPos == null)
            return;

        if (panel.Hacked)
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#94a3b8>Эта панель уже взломана.</color>", 2f, "hackers", 20);
            return;
        }

        if (IsActive)
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#94a3b8>OMEGA уже запущен.</color>", 2f, "hackers", 20);
            return;
        }

        if (_activeHackPanelId != null)
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#facc15>Уже идёт взлом панели!</color>", 2f, "hackers", 20);
            return;
        }

        if (!IsHacker(player))
        {
            if (IsSquadMember(player))
            {
                player.ShowZoneHint(HintZone.Notification,
                    "<color=#facc15>Вы — Охранник. Только Хакер может взламывать панели.\nВаша задача — защищать его!</color>", 3f, "hackers", 20);
            }
            else
            {
                player.ShowZoneHint(HintZone.Notification,
                    "<color=#94a3b8>Панель защищена системой доступа.</color>", 2f, "hackers", 20);
            }
            return;
        }

        _activeHackPanelId = panel.Id;
        SetPanelGlowColor(panel, new Color32(251, 191, 36, 255), 4.5f);

        int hackedCount = _panels.Count(p => p.Hacked);
        player.ShowZoneHint(HintZone.Notification,
            $"<color=#a78bfa>💻 Взлом панели «{panel.DisplayName}»...\n" +
            $"<size=70%>Прогресс: {hackedCount}/4 панелей взломано</color></size>", 3f, "hackers", 22);

        _activeHackCoroutine = Timing.RunCoroutine(PanelHackCoroutine(player, panel), $"{HackTag}{panel.Id}");
    }

    private IEnumerator<float> PanelHackCoroutine(Player hacker, HackPanel panel)
    {
        float tick = Mathf.Max(0.5f, _config.HackTickSeconds);
        int progress = 0;
        _activeHackProgress = 0;
        var panelPos = panel.WorldPos ?? Vector3.zero;

        while (progress < 30) // 30 тиков × 1с = 30с на панель
        {
            yield return Timing.WaitForSeconds(tick);

            if (!hacker.IsConnected || !hacker.IsAlive)
            {
                ResetPanelHack("хакер выбыл");
                yield break;
            }

            if (panel.WorldPos == null ||
                (hacker.Position - panel.WorldPos.Value).sqrMagnitude >
                _config.HackRadius * _config.HackRadius)
            {
                ResetPanelHack("хакер отошёл");
                yield break;
            }

            progress++;
            _activeHackProgress = progress;
        }

        // Панель взломана!
        panel.Hacked = true;
        _activeHackPanelId = null;
        _activeHackProgress = 0;
        SetPanelGlowColor(panel, new Color32(74, 222, 128, 255), 4.0f);

        NoRulesPlugin.Instance?.PlayerXp?.SetRawXp(hacker.UserId, string.Empty,
            NoRulesPlugin.Instance.PlayerXp.GetXp(hacker.UserId) + 100f);

        int total = _panels.Count(p => p.Hacked);

        BroadcastToAll(
            $"<color=#a78bfa>💻 Хакеры взломали панель «{panel.DisplayName}» ({total}/4)</color>\n" +
            "<size=65%><color=#6f6f6f>Не дайте им взломать остальные!</color></size>");

        try { Exiled.API.Features.Cassie.Message("SCPTERMINATION UNKNOWN . SECURITY BREACH", false, false, false); } catch { }

        // Проверяем: все 4 взломаны?
        if (_panels.All(p => p.Hacked))
        {
            AllPanelsHacked();
        }
    }

    private void ResetPanelHack(string reason)
    {
        _activeHackProgress = 0;
        if (_activeHackCoroutine.IsRunning)
            Timing.KillCoroutines(_activeHackCoroutine);
        Timing.KillCoroutines("hackers_hack");
        _activeHackPanelId = null;

        foreach (var p in _panels.Where(p => !p.Hacked))
            SetPanelGlowColor(p, new Color32(167, 139, 250, 255), 2.5f);

        if (!string.IsNullOrEmpty(reason))
            BroadcastToAll($"<color=#f87171>💥 Взлом прерван ({reason})!</color>");
    }

    private static void SetPanelGlowColor(HackPanel? panel, Color32 color, float intensity)
    {
        try
        {
            if (panel?.Glow != null)
            {
                panel.Glow.Color = color;
                panel.Glow.Intensity = intensity;
            }
        }
        catch { }
    }

    // ------------------------------------------------------------------
    //  Все панели взломаны → Взлом входной гермодвери Комнаты управления
    // ------------------------------------------------------------------

    private void AllPanelsHacked()
    {
        BroadcastToAll(
            "<color=#f59e0b><b>⚠️ ВСЕ 4 ТЕРМИНАЛА ВЗЛОМАНЫ!</b></color>\n" +
            "<color=#a78bfa>Протокол изоляции ослаблен! Хакеры должны взломать входную гермодверь Комнаты Управления (HCZ 049 / Зона 173) клавишей [E].</color>");

        try
        {
            Exiled.API.Features.Cassie.Message(
                "ATTENTION . ALL SERVER SYSTEMS COMPROMISED . HCZ CONTROL ROOM PROTOCOL LOCK OVERRIDDEN", false, false, false);
        }
        catch { }
    }

    private void StartDoorHack(Player hacker, Door door)
    {
        if (_isHackingDoor || _doorHacked) return;
        _isHackingDoor = true;

        // CASSIE тревога о попытке взлома комнаты управления
        try
        {
            Exiled.API.Features.Cassie.Message(
                "ATTENTION . SECURITY ALERT . ATTEMPTED BREACH OF HEAVY CONTAINMENT CONTROL ROOM DETECTED", false, false, false);
        }
        catch { }

        BroadcastToAll(
            "<color=#ef4444><b>🚨 ВНИМАНИЕ: ХАКЕРЫ НАЧАЛИ ВЗЛОМ ВХОДНОЙ ГЕРМОДВЕРИ КОМНАТЫ УПРАВЛЕНИЯ!</b></color>\n" +
            "<size=65%><color=#c2c2c2>Перехватите взломщика в HCZ (зона 049 / 173) до снятия блокировки!</color></size>");

        _activeDoorCoroutine = Timing.RunCoroutine(DoorHackCoroutine(hacker, door), $"{HackTag}door");
    }

    private IEnumerator<float> DoorHackCoroutine(Player hacker, Door door)
    {
        int progress = 0;
        _activeDoorProgress = 0;
        const int required = 25; // 25 секунд на взлом двери
        Vector3 hackPos = hacker.Position;

        hacker.ShowZoneHint(HintZone.TopCenter,
            "<color=#a78bfa><b>💻 ВЗЛОМ ГЕРМОДВЕРИ КОМНАТЫ УПРАВЛЕНИЯ...</b></color>\n" +
            "<size=70%><color=#c2c2c2>Удерживайте позицию у двери! Взлом займёт 25 секунд.</color></size>",
            4f, "door_hack_start", 25);

        while (progress < required)
        {
            yield return Timing.WaitForSeconds(1f);

            if (!hacker.IsConnected || !hacker.IsAlive)
            {
                ResetDoorHack("хакер погиб");
                yield break;
            }

            if ((hacker.Position - hackPos).sqrMagnitude > 36f) // дальше 6 метров
            {
                ResetDoorHack("хакер отошёл от двери");
                yield break;
            }

            progress++;
            _activeDoorProgress = progress;
        }

        // Дверь успешно взломана!
        _doorHacked = true;
        _isHackingDoor = false;
        _activeDoorProgress = 0;

        UnlockControlRoomDoors();
        BuildControlRoom();

        try
        {
            Exiled.API.Features.Cassie.Message(
                "ATTENTION . SECURITY OVERRIDE . CONTROL ROOM COMPROMISED . MAIN TERMINAL VULNERABLE", false, false, false);
        }
        catch { }

        BroadcastToAll(
            "<color=#22c55e><b>🔓 ГЕРМОДВЕРЬ КОМНАТЫ УПРАВЛЕНИЯ ВЗЛОМАНА!</b></color>\n" +
            "<color=#a78bfa>Хакеры получили доступ к Главному Компьютеру! Запустите протокол OMEGA WARHEAD!</color>");
    }

    private void ResetDoorHack(string reason)
    {
        _isHackingDoor = false;
        _activeDoorProgress = 0;
        if (_activeDoorCoroutine.IsRunning)
            Timing.KillCoroutines(_activeDoorCoroutine);

        if (!string.IsNullOrEmpty(reason))
            BroadcastToAll($"<color=#f87171>💥 Взлом гермодвери прерван ({reason})!</color>");
    }

    // ------------------------------------------------------------------
    //  Комната управления
    // ------------------------------------------------------------------

    private void StartControlHack(Player player)
    {
        if (_controlRoom == null || _controlRoom.Hacked || IsActive)
            return;

        if (!_panels.All(p => p.Hacked))
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#facc15>Сначала взломайте все 4 панели!</color>", 2.5f, "hackers", 20);
            return;
        }

        if (!IsHacker(player))
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#94a3b8>Только Хакер может взломать Комнату Управления.</color>", 2.5f, "hackers", 20);
            return;
        }

        _activeControlCoroutine = Timing.RunCoroutine(ControlHackCoroutine(player), $"{HackTag}control");
    }

    private IEnumerator<float> ControlHackCoroutine(Player hacker)
    {
        var panelPos = _controlRoom?.WorldPos ?? Vector3.zero;
        int progress = 0;
        _activeControlProgress = 0;
        const int required = 100; // 100 тиков × 1с = ~100 секунд

        hacker.ShowZoneHint(HintZone.TopCenter,
            "<color=#a78bfa><b>💻 ВЗЛОМ КОМНАТЫ УПРАВЛЕНИЯ</b></color>\n" +
            "<size=70%><color=#c2c2c2>Не отходите! Это займёт ~100 секунд.</color></size>", 4f, "hackers", 22);

        while (progress < required)
        {
            yield return Timing.WaitForSeconds(1f);

            if (!hacker.IsConnected || !hacker.IsAlive)
            {
                ResetControlHack("хакер выбыл");
                yield break;
            }

            if (_controlRoom?.WorldPos == null ||
                (hacker.Position - _controlRoom.WorldPos.Value).sqrMagnitude >
                _config.HackRadius * _config.HackRadius)
            {
                ResetControlHack("хакер отошёл");
                yield break;
            }

            progress += 1;
            _activeControlProgress = progress;

            if (progress == 50)
            {
                AlertFacility();
            }
        }

        CompleteControlHack(hacker);
    }

    private void ResetControlHack(string reason)
    {
        _activeControlProgress = 0;
        if (_activeControlCoroutine.IsRunning)
            Timing.KillCoroutines(_activeControlCoroutine);
        Timing.KillCoroutines($"{HackTag}control");
        BroadcastToAll($"<color=#f87171>💥 Взлом Комнаты Управления прерван ({reason})!</color>");
    }

    private void AlertFacility()
    {
        BroadcastToAll("<color=red><b>⚠ ВНИМАНИЕ ВСЕМУ ПЕРСОНАЛУ</b></color>\n" +
            "<size=70%><color=#6f6f6f>Обнаружена попытка взлома Комнаты Управления.\n" +
            "Требуется немедленная реакция средств самообороны!</color></size>");

        try { Exiled.API.Features.Cassie.Message(_config.CassieAlert, false, false, false); } catch { }
    }

    private void CompleteControlHack(Player hacker)
    {
        _activeControlProgress = 0;
        if (_controlRoom != null) _controlRoom.Hacked = true;
        IsActive = true;

        NoRulesPlugin.Instance?.PlayerXp?.SetRawXp(hacker.UserId, string.Empty,
            NoRulesPlugin.Instance.PlayerXp.GetXp(hacker.UserId) + _config.MissionXp);

        BroadcastToAll("<color=#a78bfa><b>💀 СИСТЕМЫ КОМПЛЕКСА ВЗЛОМАНЫ</b></color>\n" +
            "<size=70%><color=#ef4444>Инициируется протокол OMEGA WARHEAD...</color></size>");

        try
        {
            Exiled.API.Features.Cassie.Message(
                "PISTAL . PROTOCOL OMEGA WARHEAD ACTIVATED . ALL PERSONNEL WILL BE TERMINATED",
                false, false, false);
        }
        catch { }

        Timing.RunCoroutine(OmegaSequence(), OmegaTag);
    }

    // ------------------------------------------------------------------
    //  HUD Статуса Взлома (Над полоской HP для Хакеров / Охранников)
    // ------------------------------------------------------------------

    private void ClearAllHackerHints()
    {
        foreach (var kvp in _hackerHints)
        {
            var pl = Player.Get(kvp.Key);
            if (pl != null)
            {
                try { PlayerDisplay.Get(pl).RemoveHint(kvp.Value); } catch { }
            }
        }
        _hackerHints.Clear();
    }

    private float GetDynamicHudY(Player player)
    {
        float baseY = 950f;
        float offset = 0f;

        if (player == null || player.ReferenceHub == null) return baseY;

        try
        {
            var stats = player.ReferenceHub.playerStats;
            if (stats != null)
            {
                var stamina = stats.GetModule<StaminaStat>();
                if (stamina != null && stamina.CurValue < stamina.MaxValue)
                {
                    offset += 32f;
                }
            }

            if (player.ArtificialHealth > 0f)
            {
                offset += 42f;
            }

            if (player.HumeShield > 0f)
            {
                offset += 42f;
            }
        }
        catch { }

        return baseY - offset;
    }

    private IEnumerator<float> HackersHudLoop()
    {
        while (true)
        {
            yield return Timing.WaitForSeconds(0.3f);

            if (_squadMembers.Count == 0 && _hackerHints.Count == 0) continue;

            string hudText = BuildHackersStatusHud();
            var activeIds = new HashSet<int>();

            foreach (var userId in _squadMembers)
            {
                var player = Player.Get(userId);
                if (player == null || !player.IsConnected || !player.IsAlive) continue;

                activeIds.Add(player.Id);
                var display = PlayerDisplay.Get(player);
                float targetY = GetDynamicHudY(player);

                if (!_hackerHints.TryGetValue(player.Id, out var hint))
                {
                    hint = new CustomHint
                    {
                        Text = hudText,
                        FontSize = 17,
                        Alignment = HintAlignment.Left,
                        XCoordinate = -332f,
                        YCoordinate = targetY,
                        YCoordinateAlign = HintVerticalAlign.Middle,
                        Layer = HintLayer.Notification,
                        Priority = 5,
                        Tag = "hackers_hud_panel",
                        SyncSpeed = HintSyncSpeed.Fast
                    };
                    _hackerHints[player.Id] = hint;
                    display.AddHint(hint);
                }
                else
                {
                    bool changed = false;
                    if (hint.Text != hudText)
                    {
                        hint.Text = hudText;
                        changed = true;
                    }
                    if (Math.Abs(hint.YCoordinate - targetY) > 0.5f)
                    {
                        hint.YCoordinate = targetY;
                        changed = true;
                    }

                    if (changed)
                    {
                        display.ForceUpdate(false);
                    }
                }
            }

            if (_hackerHints.Count > activeIds.Count)
            {
                var toRemove = _hackerHints.Keys.Where(k => !activeIds.Contains(k)).ToList();
                foreach (var id in toRemove)
                {
                    if (_hackerHints.TryGetValue(id, out var h))
                    {
                        var pl = Player.Get(id);
                        if (pl != null)
                        {
                            try { PlayerDisplay.Get(pl).RemoveHint(h); } catch { }
                        }
                        _hackerHints.Remove(id);
                    }
                }
            }
        }
    }

    private string BuildHackersStatusHud()
    {
        var sb = new System.Text.StringBuilder(256);
        int totalHacked = _panels.Count(p => p.Hacked);

        sb.Append("<color=#c084fc><b>💻 СТАТУС ВЗЛОМА</b></color> <color=#94a3b8>(").Append(totalHacked).Append("/4)</color>\n<size=75%>");

        foreach (var panel in _panels)
        {
            sb.Append("<color=#cbd5e1>• ").Append(panel.DisplayName).Append(":</color> ");
            if (panel.Hacked)
            {
                sb.Append("<color=#4ade80>✔ Взломан</color>\n");
            }
            else if (_activeHackPanelId == panel.Id)
            {
                int pct = Mathf.Clamp(_activeHackProgress * 100 / 30, 0, 100);
                sb.Append("<color=#facc15>⏳ Взлом ").Append(pct).Append("%</color>\n");
            }
            else
            {
                sb.Append("<color=#94a3b8>⚪ Ожидание</color>\n");
            }
        }

        sb.Append("<color=#cbd5e1>• Управление:</color> ");
        if (_omegaActive)
        {
            sb.Append("<color=#ef4444><b>🔥 OMEGA АКТИВЕН</b></color>");
        }
        else if (_controlRoom != null && _controlRoom.Hacked)
        {
            sb.Append("<color=#4ade80>✔ Взломана</color>");
        }
        else if (_activeControlCoroutine.IsRunning)
        {
            int pct = Mathf.Clamp(_activeControlProgress, 0, 100);
            sb.Append("<color=#f43f5e>⚡ Взлом OMEGA ").Append(pct).Append("%</color>");
        }
        else if (_doorHacked)
        {
            sb.Append("<color=#38bdf8>🔓 Терминал готов</color>");
        }
        else if (_isHackingDoor)
        {
            int pct = Mathf.Clamp(_activeDoorProgress * 100 / 25, 0, 100);
            sb.Append("<color=#facc15>🔑 Взлом двери ").Append(pct).Append("%</color>");
        }
        else if (totalHacked == _panels.Count)
        {
            sb.Append("<color=#f59e0b>🚪 Взломайте дверь [E]</color>");
        }
        else
        {
            sb.Append("<color=#ef4444>🔒 Заблокировано</color>");
        }

        sb.Append("</size>");
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    //  Aura & Radiation Helpers
    // ------------------------------------------------------------------

    private void AttachRadiationAura(Player player)
    {
        if (player == null || !player.IsConnected || !player.IsAlive) return;

        RemoveRadiationAura(player.UserId);

        try
        {
            var light = Light.Create(
                position: player.Position + Vector3.up * 1f,
                rotation: Vector3.zero,
                scale: Vector3.one,
                spawn: true,
                color: new Color32(34, 197, 94, 255)
            );

            if (light != null)
            {
                light.Intensity = 2.0f;
                light.Range = 3.5f;
                light.Transform.SetParent(player.Transform, true);
                _radiationAuras[player.UserId] = light;
            }

            AudioToggle.CreateForPlayer(player, "geigercounter", min: 0f, max: 8f, volume: 0.85f, loop: true);
        }
        catch (Exception ex)
        {
            Log.Debug($"[Hackers] Не удалось создать ауру радиации: {ex}");
        }
    }

    private void RemoveRadiationAura(string userId)
    {
        if (_radiationAuras.TryGetValue(userId, out var light))
        {
            try
            {
                light?.Destroy();
            }
            catch { }
            _radiationAuras.Remove(userId);
        }

        var pl = Player.Get(userId);
        if (pl != null)
        {
            try { AudioToggle.DestroyForPlayer(pl); } catch { }
        }
    }

    private void CureRadiation(Player? player)
    {
        if (player == null) return;
        string userId = player.UserId;
        _radiationSickPlayers.Remove(userId);
        _timeInRadiation.Remove(userId);
        _timeInCleanRoom.Remove(userId);
        RemoveRadiationAura(userId);
        try { player.DisableEffect(EffectType.Slowness); } catch { }
    }

    private float GetRadiationDamageForPlayer(Player player)
    {
        if (!player.IsScp)
            return _config.RadiationDamage;

        return player.Role.Type switch
        {
            RoleTypeId.Scp106 => _config.RadiationDamageScp106,
            RoleTypeId.Scp049 => _config.RadiationDamageScp049,
            RoleTypeId.Scp939 => _config.RadiationDamageScp939,
            RoleTypeId.Scp096 => _config.RadiationDamageScp096,
            RoleTypeId.Scp0492 => _config.RadiationDamageScp0492,
            RoleTypeId.Scp3114 => _config.RadiationDamageScp3114,
            RoleTypeId.Scp173 => _config.RadiationDamageScp173,
            _ => _config.RadiationDamageScpDefault
        };
    }

    // ------------------------------------------------------------------
    //  Omega Warhead & Radiation Survival
    // ------------------------------------------------------------------

    private IEnumerator<float> OmegaSequence()
    {
        _omegaActive = true;
        float elapsed = 0f;
        bool pulseBright = false;

        // Немедленно останавливаем и блокируем спавны любых волн подкреплений
        try { SpawnManager.StopSpawning(); } catch { }

        SnapshotLights();

        // Запуск глобального саундтрека OMEGA Warhead (рассчитан ровно на 2 мин 39 сек)
        try { AudioToggle.CreateGlobal("omegawarheadmusic", volume: 1.0f); } catch { }

        // Блокируем Альфа-боеголовку намертво
        try
        {
            Warhead.Stop();
            Warhead.IsLocked = true;
        }
        catch { }

        // Блокируем лифты Gate A и Gate B на время отсчета
        try
        {
            foreach (var door in Door.List.Where(d =>
                d.Type is DoorType.ElevatorGateA or DoorType.ElevatorGateB))
            {
                door.IsOpen = false;
                door.ChangeLock(DoorLockType.Regular079);
                _lockedDoors.Add(door);
            }
        }
        catch { }

        // 2. Обратный отсчёт (Динамичный и постоянный каждую секунду)
        while (elapsed < _config.OmegaCountdownSeconds)
        {
            yield return Timing.WaitForSeconds(1f);
            elapsed += 1f;
            pulseBright = !pulseBright;

            SetCountdownLights(pulseBright);

            int remaining = (int)(_config.OmegaCountdownSeconds - elapsed);
            if (remaining < 0) remaining = 0;

            int min = remaining / 60;
            int sec = remaining % 60;
            string color = remaining <= 30 ? "#ef4444" : "#f59e0b";

            // Постоянный живой HUD для каждого игрока каждую секунду
            foreach (var pl in Player.List.Where(p => p != null && p.IsConnected))
            {
                pl.ShowZoneHint(HintZone.TopCenter,
                    $"<color={color}><b>[ OMEGA WARHEAD ]</b></color> <color=#f87171>до детонации <b>{min}:{sec:D2}</b></color>\n" +
                    $"<size=70%><color=#ffd285>Единственное безопасное место — <b>ТЯЖЁЛАЯ ЗОНА (HCZ)</b>!</color></size>",
                    1.2f, "omega_countdown", 26);
            }
        }

        // 3. МОМЕНТ ДЕТОНАЦИИ
        _omegaDetonated = true;
        // Саундтрек продолжает играть до своего финального аккорда и затухания

        // Мощный полномасштабный эффект взрыва боеголовки и тряска экрана
        try { Map.WarheadExplosionEffect(true); } catch { }
        try { Warhead.Shake(true); } catch { }

        BroadcastToAll("<color=#ef4444><b>[ ДЕТОНАЦИЯ OMEGA БОЕГОЛОВКИ ]</b></color>\n" +
                       "<size=75%><color=#cbd5e1>Поверхность, Офисы и Лайтзона уничтожены!\nТяжёлая Зона изолирована.</color></size>");

        try
        {
            Exiled.API.Features.Cassie.Message(
                "PITCH_0.2 .G4 .G4 .G4 ATTENTION ALL PERSONNEL . OMEGA WARHEAD DETONATED . SURFACE . ENTRANCE AND LIGHT CONTAINMENT ZONES COMPROMISED . HEAVY CONTAINMENT ZONE ISOLATED",
                false, false, true);
        }
        catch { }

        // Уничтожаем всех, кто НЕ в HCZ
        foreach (var pl in Player.List.Where(x => x is { IsAlive: true }).ToList())
        {
            try
            {
                if (pl.Zone != ZoneType.HeavyContainment)
                {
                    Map.ExplodeEffect(pl.Position, ProjectileType.FragGrenade);
                    pl.Kill("Детонация OMEGA боеголовки");
                }
                else
                {
                    // Эффект детонации рядом для выживших в HCZ
                    Map.ExplodeEffect(pl.Position + Vector3.up * 4f, ProjectileType.FragGrenade);
                    pl.ShowZoneHint(HintZone.TopCenter,
                        "<color=#22c55e><b>[ ВЫ ВЫЖИЛИ В ТЯЖЁЛОЙ ЗОНЕ ]</b></color>\n" +
                        "<size=70%><color=#cbd5e1>Комплекс уничтожен. Выходы заблокированы.\nОстерегайтесь просачивающейся радиации!</color></size>",
                        10f, "omega_survive", 26);
                }
            }
            catch { }
        }

        // Блокируем периметр HCZ (гермоворота на чекпоинтах в офисы, лифты в LCZ, но НЕ сами коридоры HCZ!)
        LockHczPerimeter();

        // Устанавливаем стабильный мягкий синий свет во всей HCZ
        SetPostOmegaLights();

        // Запуск волны распространения радиации
        if (_radiationSpreadCoroutine.IsRunning)
            Timing.KillCoroutines(_radiationSpreadCoroutine);
        _radiationSpreadCoroutine = Timing.RunCoroutine(RadiationSpreadCoroutine(), $"{HackTag}rad_spread");

        // Запуск цикла лучевой болезни
        if (_radiationSicknessCoroutine.IsRunning)
            Timing.KillCoroutines(_radiationSicknessCoroutine);
        _radiationSicknessCoroutine = Timing.RunCoroutine(RadiationSicknessLoop(), $"{HackTag}rad_sickness");

        // Запуск 15-минутного таймера деконтаминации HCZ
        if (_hczDeconCoroutine.IsRunning)
            Timing.KillCoroutines(_hczDeconCoroutine);
        _hczDeconCoroutine = Timing.RunCoroutine(HczDecontaminationSequence(), $"{HackTag}hcz_decon");
    }

    private void LockHczPerimeter()
    {
        try
        {
            foreach (var door in Door.List)
            {
                if (door == null) continue;

                // 1. Гермоворота на чекпоинтах в офисы (НЕ сами чекпоинты!)
                if (door.Type is DoorType.CheckpointGateA or DoorType.CheckpointGateB or DoorType.GateA or DoorType.GateB)
                {
                    door.IsOpen = false;
                    door.ChangeLock(DoorLockType.Warhead);
                    _lockedDoors.Add(door);
                }

                // 2. Лифты в LCZ и на Поверхность
                if (door.Type is DoorType.ElevatorLczA or DoorType.ElevatorLczB or DoorType.ElevatorGateA or DoorType.ElevatorGateB)
                {
                    door.IsOpen = false;
                    door.ChangeLock(DoorLockType.Warhead);
                    _lockedDoors.Add(door);
                }

                // 3. Все двери за пределами HCZ (в EZ, LCZ, Surface) наглухо запираются
                if (door.Zone is ZoneType.Entrance or ZoneType.LightContainment or ZoneType.Surface)
                {
                    door.IsOpen = false;
                    door.ChangeLock(DoorLockType.Warhead);
                    _lockedDoors.Add(door);
                }
            }
        }
        catch { }
    }

    private static bool AreRoomsAdjacent(Room a, Room b)
    {
        if (a == null || b == null || a == b) return false;

        try
        {
            if (a.Doors != null && b.Doors != null)
            {
                foreach (var da in a.Doors)
                {
                    if (da != null && b.Doors.Contains(da))
                        return true;
                }
            }
        }
        catch { }

        float distSq = (a.Position - b.Position).sqrMagnitude;
        return distSq <= 24f * 24f;
    }

    private IEnumerator<float> RadiationSpreadCoroutine()
    {
        _contaminatedRooms.Clear();

        // Начальные очаги: чекпоинты HCZ
        var checkpointRooms = Room.List.Where(r =>
            r.Zone == ZoneType.HeavyContainment &&
            (r.Type is RoomType.HczEzCheckpointA or RoomType.HczEzCheckpointB)).ToList();

        if (checkpointRooms.Count == 0)
        {
            checkpointRooms = Room.List.Where(r => r.Zone == ZoneType.HeavyContainment).Take(2).ToList();
        }

        foreach (var r in checkpointRooms)
        {
            _contaminatedRooms.Add(r);
            try { r.Color = RadiationGreenLight; } catch { }
            try { AudioToggle.CreateForRoom(r, "geigercounter", min: 1f, max: 15f, volume: 0.6f, loop: true); } catch { }
        }

        BroadcastToAll("<color=#22c55e><b>[ ВНИМАНИЕ: РАДИАЦИЯ ]</b></color>\n" +
                       "<size=70%><color=#94a3b8>Радиация из офисов просачивается в HCZ. Чекпоинты заражены.</color></size>");

        while (true)
        {
            yield return Timing.WaitForSeconds(_config.RadiationSpreadIntervalSeconds);

            if (!_omegaDetonated) yield break;

            // Все комнаты HCZ, еще не зараженные
            var cleanHczRooms = Room.List.Where(r =>
                r.Zone == ZoneType.HeavyContainment &&
                !_contaminatedRooms.Contains(r)).ToList();

            if (cleanHczRooms.Count == 0)
            {
                // Вся HCZ заражена
                continue;
            }

            var newlyInfected = new HashSet<Room>();

            // Для каждой уже заражённой комнаты проверяем все смежные проходы в чистые комнаты
            foreach (var contRoom in _contaminatedRooms.ToList())
            {
                var adjacentClean = cleanHczRooms
                    .Where(clean => !newlyInfected.Contains(clean) && AreRoomsAdjacent(contRoom, clean))
                    .ToList();

                foreach (var neighbor in adjacentClean)
                {
                    // На каждый проход в соседнюю чистую комнату применяется 50% шанс заражения
                    if (Random.value <= _config.RadiationSpreadChance)
                    {
                        newlyInfected.Add(neighbor);
                    }
                }
            }

            // Ограничиваем скорость: не более 1-2 смежных комнат за один такт распространения
            var roomsToInfect = newlyInfected.OrderBy(_ => Random.value).Take(2).ToList();

            foreach (var newContaminated in roomsToInfect)
            {
                _contaminatedRooms.Add(newContaminated);
                try
                {
                    newContaminated.Color = RadiationGreenLight;
                }
                catch { }
                try { AudioToggle.CreateForRoom(newContaminated, "geigercounter", min: 1f, max: 15f, volume: 0.6f, loop: true); } catch { }
            }
        }
    }

    private IEnumerator<float> RadiationSicknessLoop()
    {
        float damageTickTimer = 0f;

        while (true)
        {
            yield return Timing.WaitForSeconds(1.0f);

            if (!_omegaDetonated) yield break;

            damageTickTimer += 1.0f;
            bool applyDamageTick = damageTickTimer >= _config.RadiationDamageIntervalSeconds;
            if (applyDamageTick) damageTickTimer = 0f;

            var alivePlayers = Player.List.Where(p => p is { IsAlive: true }).ToList();

            foreach (var player in alivePlayers)
            {
                string userId = player.UserId;
                var room = player.CurrentRoom;

                bool inIrradiatedRoom = room != null && _contaminatedRooms.Contains(room);
                bool isSick = _radiationSickPlayers.Contains(userId);

                if (inIrradiatedRoom)
                {
                    if (!_timeInRadiation.ContainsKey(userId))
                        _timeInRadiation[userId] = 0f;

                    _timeInRadiation[userId] += 1.0f;
                    _timeInCleanRoom[userId] = 0f;

                    if (!isSick)
                    {
                        int cur = (int)_timeInRadiation[userId];
                        int max = (int)_config.ContaminationThresholdSeconds;
                        int filled = Mathf.Clamp(cur, 0, max);
                        string bar = new string('■', filled) + new string('□', Mathf.Max(0, max - filled));

                        // Постоянный динамичный индикатор накопления радиации (только прогресс-бар)
                        player.ShowZoneHint(HintZone.BottomCenter,
                            $"<color=#22c55e><b>[ РАДИОАКТИВНАЯ ЗОНА ]</b></color> <color=#86efac>[{bar}]</color>",
                            1.2f, "rad_status", 22);

                        if (_timeInRadiation[userId] >= _config.ContaminationThresholdSeconds)
                        {
                            _radiationSickPlayers.Add(userId);
                            AttachRadiationAura(player);

                            player.ShowZoneHint(HintZone.TopCenter,
                                "<color=#ef4444><b>[ ОСТРАЯ ЛУЧЕВАЯ БОЛЕЗНЬ ]</b></color>\n" +
                                "<size=70%><color=#cbd5e1>-20 HP каждые 7 сек • +25% получаемого урона • Легкое истощение\n" +
                                "Для излечения: примите SCP-500 или проведите 30 сек в чистой комнате</color></size>",
                                6f, "rad_sick_alert", 26);
                        }
                    }
                }
                else
                {
                    // В чистой комнате
                    if (!isSick)
                    {
                        // Не болен: радиация медленно выветривается (0.5с в секунду) вместо мгновенного сброса
                        if (_timeInRadiation.TryGetValue(userId, out float radTime) && radTime > 0f)
                        {
                            radTime = Mathf.Max(0f, radTime - 0.5f);
                            _timeInRadiation[userId] = radTime;

                            if (radTime > 0.5f)
                            {
                                int cur = (int)radTime;
                                int max = (int)_config.ContaminationThresholdSeconds;
                                int filled = Mathf.Clamp(cur, 0, max);
                                string bar = new string('■', filled) + new string('□', Mathf.Max(0, max - filled));

                                player.ShowZoneHint(HintZone.BottomCenter,
                                    $"<color=#eab308><b>[ ВЫВЕДЕНИЕ РАДИАЦИИ ]</b></color> <color=#fde047>[{bar}]</color>",
                                    1.2f, "rad_status", 22);
                            }
                        }
                    }
                    else
                    {
                        // Болен лучевой болезнью: нужно 30 секунд непрерывно в чистой комнате
                        _timeInRadiation[userId] = 0f;

                        if (!_timeInCleanRoom.ContainsKey(userId))
                            _timeInCleanRoom[userId] = 0f;

                        _timeInCleanRoom[userId] += 1.0f;

                        int cleanCur = (int)_timeInCleanRoom[userId];
                        int cleanMax = (int)_config.RadiationRecoverySeconds;
                        int filled = Mathf.Clamp(cleanCur * 10 / Mathf.Max(1, cleanMax), 0, 10);
                        string cleanBar = new string('■', filled) + new string('□', Mathf.Max(0, 10 - filled));

                        // Постоянный динамичный индикатор восстановления (только прогресс-бар)
                        player.ShowZoneHint(HintZone.BottomCenter,
                            $"<color=#38bdf8><b>[ ЧИСТАЯ ЗОНА ]</b></color> <color=#bae6fd>[{cleanBar}]</color>\n" +
                            $"<size=70%><color=#94a3b8>Оставайтесь в чистой комнате для полного выздоровления</color></size>",
                            1.2f, "rad_status", 22);

                        if (_timeInCleanRoom[userId] >= _config.RadiationRecoverySeconds)
                        {
                            CureRadiation(player);
                            player.ShowZoneHint(HintZone.TopCenter,
                                "<color=#22c55e><b>[ ОРГАНИЗМ ПОЛНОСТЬЮ ОЧИЩЕН ]</b></color>\n" +
                                "<size=70%><color=#86efac>Лучевая болезнь отступила!</color></size>",
                                5f, "rad_cured", 26);
                        }
                    }
                }

                // При наличии активной лучевой болезни
                if (_radiationSickPlayers.Contains(userId))
                {
                    // Замедляем ТОЛЬКО людей, стамину не трогаем (EffectType.Slowness)
                    if (!player.IsScp)
                    {
                        try
                        {
                            player.EnableEffect(EffectType.Slowness, 15, 2.0f);
                        }
                        catch { }
                    }

                    if (applyDamageTick)
                    {
                        try
                        {
                            float dmg = GetRadiationDamageForPlayer(player);
                            float hs = player.HumeShield;
                            float ahp = player.ArtificialHealth;

                            // Наносим урон через хендлер урона (Exiled DamageHandler) для корректного поглощения щитами
                            player.Hurt(dmg, DamageType.Custom, "Острая лучевая болезнь");

                            string dmgLabel;
                            if (player.IsScp && hs > 0)
                            {
                                dmgLabel = $"<color=#818cf8><b>[ ЛУЧЕВАЯ БОЛЕЗНЬ ]</b> -{(int)dmg} HS</color>";
                            }
                            else if (ahp > 0)
                            {
                                dmgLabel = $"<color=#38bdf8><b>[ ЛУЧЕВАЯ БОЛЕЗНЬ ]</b> -{(int)dmg} AHP</color>";
                            }
                            else
                            {
                                dmgLabel = $"<color=#ef4444><b>[ ЛУЧЕВАЯ БОЛЕЗНЬ ]</b> -{(int)dmg} HP</color>";
                            }

                            player.ShowZoneHint(HintZone.BottomCenter, dmgLabel, 1.5f, "rad_status", 24);
                        }
                        catch { }
                    }
                    else if (inIrradiatedRoom)
                    {
                        // Если игрок уже болеет и всё ещё в радиации
                        float dmg = GetRadiationDamageForPlayer(player);
                        player.ShowZoneHint(HintZone.BottomCenter,
                            $"<color=#ef4444><b>[ ОСТРАЯ ЛУЧЕВАЯ БОЛЕЗНЬ ]</b></color> <color=#fca5a5>-{(int)dmg} HP каждые 7с • +25% урона</color>",
                            1.2f, "rad_status", 22);
                    }
                }
            }
        }
    }

    private IEnumerator<float> HczDecontaminationSequence()
    {
        float totalWait = _config.HczDeconTimeSeconds; // 900 сек (15 мин)

        // 1 минута до деконтаминации
        if (totalWait > 60f)
        {
            yield return Timing.WaitForSeconds(totalWait - 60f);

            if (!_omegaDetonated) yield break;

            BroadcastToAll("<color=#ef4444><b>[ ВНИМАНИЕ ]</b></color> <color=#cbd5e1>До полной очистки Тяжёлой Зоны осталась 1 минута!</color>");
            try
            {
                Exiled.API.Features.Cassie.Message(
                    "ATTENTION ALL PERSONNEL . HEAVY CONTAINMENT ZONE DECONTAMINATION IN 1 MINUTE",
                    false, false, true);
            }
            catch { }

            yield return Timing.WaitForSeconds(30f);
            if (!_omegaDetonated) yield break;

            try { Exiled.API.Features.Cassie.Message("30 SECONDS REMAINING", false, false, false); } catch { }

            yield return Timing.WaitForSeconds(30f);
        }
        else
        {
            yield return Timing.WaitForSeconds(totalWait);
        }

        if (!_omegaDetonated) yield break;

        _hczDecontaminated = true;
        BroadcastToAll("<color=#ef4444><b>[ ПРОТОКОЛ ОЧИСТКИ ТЯЖЁЛОЙ ЗОНЫ АКТИВИРОВАН ]</b></color>\n" +
                       "<size=70%><color=#cbd5e1>Смертельный газ заполняет весь сектор!</color></size>");

        try
        {
            Exiled.API.Features.Cassie.Message(
                "HEAVY CONTAINMENT ZONE DECONTAMINATION PROTOCOL ACTIVATED . LETHAL GAS RELEASED",
                false, false, true);
        }
        catch { }

        // Мигающий аварийный свет
        SetAllRoomsColor(new Color32(220, 20, 20, 255));

        while (_omegaDetonated)
        {
            yield return Timing.WaitForSeconds(1.0f);

            var alive = Player.List.Where(p => p is { IsAlive: true }).ToList();
            if (alive.Count == 0) break;

            foreach (var p in alive)
            {
                try
                {
                    float dps = p.IsScp ? _config.HczDeconScpDps : _config.HczDeconHumanDps;
                    if (p.Health > dps)
                    {
                        p.Health -= dps;
                    }
                    else
                    {
                        p.Kill("Протокол очистки HCZ");
                    }

                    p.ShowZoneHint(HintZone.TopCenter,
                        $"<color=#ef4444><b>[ ПРОТОКОЛ ОЧИСТКИ HCZ ]</b></color> <color=#f87171>Смертельный газ! (-{(int)dps} HP/с)</color>",
                        1.2f, "hcz_decon_active", 26);
                }
                catch { }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Свет / утилиты (Синий свет в HCZ, мигающий оранжевый в комплексе)
    // ------------------------------------------------------------------

    // Цветовая схема OMEGA освещения:
    // Хардзона — мягкий комфортный синий свет с естественной видимостью без нагрузки на глаза
    private static readonly Color32 HczBlueLight = new(45, 120, 220, 255);
    // Внешние сектора (Офисы, Лайтзона, Поверхность) — тревожный, мягко мигающий оранжевый
    private static readonly Color32 WarningOrangeBright = new(220, 115, 20, 255);
    private static readonly Color32 WarningOrangeDim = new(65, 30, 5, 255);
    // Радиоактивные заражённые комнаты — мягкий зелёный
    private static readonly Color32 RadiationGreenLight = new(35, 195, 55, 255);

    private void SnapshotLights()
    {
        _originalRoomColors.Clear();
        foreach (var room in Room.List)
        {
            try { _originalRoomColors[room] = room.Color; } catch { }
        }
        _lightsModified = true;
    }

    private void SetCountdownLights(bool pulseBright)
    {
        Color32 otherColor = pulseBright ? WarningOrangeBright : WarningOrangeDim;

        foreach (var room in Room.List)
        {
            try
            {
                if (room.Zone == ZoneType.HeavyContainment)
                {
                    // В Хардзоне — стабильный мягкий синий свет с нормальной видимостью
                    room.Color = HczBlueLight;
                }
                else
                {
                    // В остальном комплексе — мягко мигающий оранжевый аварийный свет
                    room.Color = otherColor;
                }
            }
            catch { }
        }
    }

    private void SetPostOmegaLights()
    {
        foreach (var room in Room.List)
        {
            try
            {
                if (room.Zone == ZoneType.HeavyContainment)
                {
                    room.Color = _contaminatedRooms.Contains(room) ? RadiationGreenLight : HczBlueLight;
                }
                else
                {
                    room.Color = WarningOrangeDim;
                }
            }
            catch { }
        }
    }

    private void SetAllRoomsColor(Color32 color)
    {
        foreach (var room in Room.List)
        {
            try { room.Color = color; } catch { }
        }
    }

    private void RestoreLights()
    {
        if (!_lightsModified) return;

        foreach (var kvp in _originalRoomColors)
        {
            try { kvp.Key.Color = kvp.Value; } catch { }
        }

        _originalRoomColors.Clear();
        _lightsModified = false;
    }

    private void BroadcastToAll(string message)
    {
        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, message, 6f, "hackers", 22);
    }

    // Legacy properties for compatibility
    public bool IsActive { get; private set; }
    public bool IsHacking => _hackers.Count > 0;
}
