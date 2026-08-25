using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.ServerSpecific;
using Capy.Engine.Studio.Core;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Doors;
using Exiled.API.Features.Toys;
using PlayerRoles;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using MEC;
using Mirror;
using UnityEngine;
using Light = Exiled.API.Features.Toys.Light;
using Random = UnityEngine.Random;

namespace Capy.NoRules.Features.Concepts.Hackers;

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
        public RoomType RoomType { get; set; }
        public Vector3 Offset { get; set; }
        public bool Hacked { get; set; }
        public Vector3? WorldPos { get; set; }
        public List<GameObject> Visuals { get; } = new();
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
    private readonly Dictionary<Room, Color32> _originalRoomColors = new();
    private bool _lightsModified;
    private bool _spawnedThisRound;
    private bool _enabled;
    private int _memberCounter;
    private DateTime _roundStartTime = DateTime.UtcNow;

    // === OMEGA ===
    private bool _omegaActive;

    private const string HackTag = "hackers_hack_";
    private const string OmegaTag = "hackers_omega";

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
            Id = "panel_junk", DisplayName = "Серверная HCZ",
            RoomType = RoomType.HczHid, Offset = new Vector3(0f, 0.8f, -1.5f)
        });
        _panels.Add(new HackPanel
        {
            Id = "panel_hid", DisplayName = "Комната турели",
            RoomType = RoomType.Hcz049, Offset = new Vector3(0f, 0.8f, -1.5f)
        });
        _panels.Add(new HackPanel
        {
            Id = "panel_nuke", DisplayName = "Ядерный зал",
            RoomType = RoomType.Hcz079, Offset = new Vector3(0f, 0.8f, -1.5f)
        });
        _panels.Add(new HackPanel
        {
            Id = "panel_079", DisplayName = "Комната 079",
            RoomType = RoomType.HczEzCheckpointA, Offset = new Vector3(0f, 0.8f, -1.5f)
        });

        _controlRoom = new HackPanel
        {
            Id = "control_room", DisplayName = "Комната Управления",
            RoomType = RoomType.HczServerRoom, Offset = new Vector3(0f, 0.8f, -1.5f)
        };
    }

    // ------------------------------------------------------------------
    //  Lifecycle
    // ------------------------------------------------------------------

    public void Enable()
    {
        if (!_config.IsEnabled) return;
        _enabled = true;

        Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
        Exiled.Events.Handlers.Player.Spawned += OnSpawned;
        Exiled.Events.Handlers.Player.ChangingRole += OnChangingRole;
        Exiled.Events.Handlers.Player.Died += OnDied;
        Exiled.Events.Handlers.Player.Left += OnLeft;
        Exiled.Events.Handlers.Player.Hurting += OnHurting;
        Exiled.Events.Handlers.Player.InteractingDoor += OnInteractingDoor;
        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
    }

    public void Disable()
    {
        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;

        Exiled.Events.Handlers.Player.Spawned -= OnSpawned;
        Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        Exiled.Events.Handlers.Player.Left -= OnLeft;
        Exiled.Events.Handlers.Player.Hurting -= OnHurting;
        Exiled.Events.Handlers.Player.InteractingDoor -= OnInteractingDoor;
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

        RestoreLights();
        TeardownVisuals();
        ResetState();
    }

    private void ResetState()
    {
        _panels.Clear();
        _controlRoom = null;
        _squadMembers.Clear();
        _kittedPlayers.Clear();
        _hackers.Clear();
        _guards.Clear();
        _hackersRank.Clear();
        _spawnProtection.Clear();
        _spawnedObjects.Clear();
        _lockedDoors.Clear();
        _originalRoomColors.Clear();
        _lightsModified = false;
        _spawnedThisRound = false;
        _omegaActive = false;
        _memberCounter = 0;
        _panelPosition = null;
        StationsManager.Unregister("hackers_panel");
        ConceptsController.Disable();
    }

    private void OnRoundStarted()
    {
        _roundStartTime = DateTime.UtcNow;
        InitPanels();
        BuildAllPanels();
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
                panel.WorldPos = pos;

                var schematic = SchematicLoader.Spawn("HackerPanel", pos, room.Rotation);
                if (schematic != null)
                {
                    foreach (var go in schematic.SpawnedGameObjects)
                    {
                        if (go != null) _spawnedObjects.Add(go);
                    }
                    foreach (var prim in schematic.SpawnedPrimitives)
                    {
                        try { if (prim?.GameObject != null) _spawnedObjects.Add(prim.GameObject); } catch { }
                    }
                }

                StationsManager.Register(
                    $"hackers_{panel.Id}",
                    pos + Vector3.up * 0.8f,
                    Mathf.Max(1.5f, _config.PanelRadius),
                    p => StartPanelHack(p, panel));
            }
            catch (Exception ex)
            {
                Log.Error($"[Hackers] Ошибка построения панели '{panel.DisplayName}': {ex}");
            }
        }

        // Комната управления — строим только после взлома всех панелей
    }

    private void BuildControlRoom()
    {
        if (_controlRoom == null) return;

        try
        {
            var room = Room.List.FirstOrDefault(r => r.Type == _controlRoom.RoomType);
            if (room == null) return;

            Vector3 pos = room.Position + room.Rotation * _controlRoom.Offset;
            _controlRoom.WorldPos = pos;

            var schematic = SchematicLoader.Spawn("HackerPanel", pos, room.Rotation);
            if (schematic != null)
            {
                foreach (var go in schematic.SpawnedGameObjects)
                {
                    if (go != null) _spawnedObjects.Add(go);
                }
            }

            StationsManager.Register(
                "hackers_control",
                pos + Vector3.up * 0.8f,
                Mathf.Max(1.5f, _config.PanelRadius),
                p => StartControlHack(p));

            // HDR-свечение комнаты управления (заметная точка)
            var glow = Light.Create(
                position: pos + Vector3.up * 1.5f,
                rotation: null, scale: Vector3.one,
                spawn: false,
                color: new Color32(167, 139, 250, 255));
            if (glow != null)
            {
                glow.Intensity = 5f;
                glow.Range = 5f;
                glow.Spawn();
                _spawnedObjects.Add(glow.GameObject);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[Hackers] Ошибка построения комнаты управления: {ex}");
        }
    }

    /// <summary>
    /// Добавляет одного игрока в группировку Хакеров (для .gcr).
    /// </summary>
    public void AddMember(Player player)
    {
        if (player == null || !player.IsAlive) return;

        _squadMembers.Add(player.UserId);
        CustomUnits.AssignMember(player, "Группировка «Хакеры»", "#a78bfa", "Хакер");

        try
        {
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

        player.ShowZoneHint(HintZone.TopCenter,
            "<color=#a78bfa><b>💻 Вы — Хакер</b></color>\n" +
            "<size=70%><color=#c2c2c2>Найдите серверную комнату и взломайте панель [E]</color></size>",
            8f, "hackers_brief", 20);
    }

    private void TeardownVisuals()
    {
        foreach (var go in _spawnedObjects)
        {
            try
            {
                if (go == null) continue;
                NetworkServer.UnSpawn(go);
                UnityEngine.Object.Destroy(go);
            }
            catch { }
        }
        _spawnedObjects.Clear();
    }

    // ------------------------------------------------------------------
    //  Формирование отряда
    // ------------------------------------------------------------------

    private Vector3? _panelPosition; // legacy

    private bool CanAutoSpawn()
    {
        if (_spawnedThisRound) return false;
        if (Random.Range(0, 100) >= Mathf.Clamp(_config.ChancePercent, 0, 100)) return false;
        return true;
    }

    private void OnRespawningTeam(RespawningTeamEventArgs ev) { } // не используем напрямую

    /// <summary>
    /// Централизованный вызов из NoRulesPlugin: сначала CO2, потом Хакеры.
    /// </summary>
    public bool TryTakeWave(RespawningTeamEventArgs ev)
    {
        if (_spawnedThisRound || !CanAutoSpawn())
            return false;

        var squad = CollectCandidates(Math.Min(_config.SquadSizeMax, ev.MaximumRespawnAmount));
        if (squad == null || squad.Count == 0)
            return false;

        ev.Players.Clear();
        ev.Players.AddRange(squad);

        RegisterSquad(squad);
        return true;
    }

    private List<Player>? CollectCandidates(int max)
    {
        var spectators = Player.List
            .Where(p => p != null && p.IsConnected &&
                        (p.Role.Type == RoleTypeId.Spectator || p.Role.Type == RoleTypeId.Overwatch))
            .ToList();

        if (spectators.Count < Math.Max(1, _config.MinSpectators))
            return null;

        for (int i = spectators.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (spectators[i], spectators[j]) = (spectators[j], spectators[i]);
        }

        return spectators.Take(max).ToList();
    }

    private void RegisterSquad(List<Player> squad)
    {
        _spawnedThisRound = true;

        for (int i = 0; i < squad.Count; i++)
        {
            var member = squad[i];
            _squadMembers.Add(member.UserId);

            bool isHacker = i == 0; // первый — хакер, остальные охрана
            if (isHacker) _hackers.Add(member.UserId);
            else _guards.Add(member.UserId);

            _spawnProtection[member.UserId] = DateTime.UtcNow.AddSeconds(10f);

            if (isHacker)
            {
                CustomUnits.AssignMember(member, "Группировка «Хакеры»", "#a78bfa", "Хакер");

                Timing.CallDelayed(0.5f, () =>
                {
                    if (member == null || !member.IsConnected) return;
                    try
                    {
                        member.ClearInventory();
                        member.AddItem(ItemType.KeycardChaosInsurgency);
                        member.AddItem(ItemType.GunE11SR);
                        member.AddItem(ItemType.GunFRMG0);
                        member.AddItem(ItemType.SCP500);
                        member.AddItem(ItemType.ArmorHeavy);
                        member.AddItem(ItemType.Radio);
                        member.AddItem(ItemType.Lantern);
                        member.AddItem(ItemType.GrenadeFlash);
                    }
                    catch { }
                });

                member.ShowZoneHint(HintZone.TopCenter,
                    "<color=#a78bfa><b>💻 Вы — Хакер группировки «Хакеры»</b></color>\n" +
                    "<size=65%><color=#c2c2c2>Повстанцы Хаоса</color></size>\n\n" +
                    "<color=#ffffff><b>МИССИЯ:</b> Взломать 4 панели по всему комплексу,\n" +
                    "затем Комнату Управления. Это запустит OMEGA WARHEAD.</color>\n\n" +
                    "<color=#ffd285>ПАНЕЛИ:</color>\n" +
                    "<size=65%><color=#c2c2c2>1. Серверная HCZ → 2. Турельная → 3. Ядерный зал → 4. Комната 079\n" +
                    "[E] рядом с панелью. НЕ ОТХОДИТЕ дальше 7м — прогресс сбросится!\n" +
                    "После всех 4 панелей идите в серверную — откроется Комната Управления.</color></size>\n\n" +
                    "<color=#a3e635>+300 XP за взлом комнаты управления</color>",
                    15f, "hackers_brief", 18);
            }
            else
            {
                CustomUnits.AssignMember(member, "Группировка «Хакеры»", "#a78bfa", "Охранник");

                Timing.CallDelayed(0.5f, () =>
                {
                    if (member == null || !member.IsConnected) return;
                    try
                    {
                        member.ClearInventory();
                        member.AddItem(ItemType.KeycardChaosInsurgency);
                        member.AddItem(ItemType.GunE11SR);
                        member.AddItem(ItemType.GunShotgun);
                        member.AddItem(ItemType.SCP500);
                        member.AddItem(ItemType.GrenadeHE);
                        member.AddItem(ItemType.GrenadeFlash);
                        member.AddItem(ItemType.Lantern);
                        member.AddItem(ItemType.ArmorHeavy);
                    }
                    catch { }
                });

                member.ShowZoneHint(HintZone.TopCenter,
                    "<color=#ef4444><b>🛡 Вы — Охранник Хакера</b></color>\n" +
                    "<size=65%><color=#c2c2c2>Повстанцы Хаоса</color></size>\n\n" +
                    "<color=#ffffff><b>МИССИЯ:</b> Защищайте Хакера пока он взламывает панели.\n" +
                    "Убивайте всех, кто приближается к панели во время взлома.\n" +
                    "Без Хакера взлом невозможен!</color>",
                    12f, "hackers_brief", 18);
            }
        }

        BroadcastToAll("<color=#a78bfa>🕶 По комплексу перемещается <b>Группировка «Хакеры»</b>...\n" +
            "<size=65%><color=#6f6f6f>Перехватите их до завершения взлома!</color></size>");
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

        if (IsScpRole(ev.NewRole) || ev.NewRole is RoleTypeId.None or RoleTypeId.Spectator or RoleTypeId.Overwatch)
        {
            if (_squadMembers.Remove(ev.Player.UserId))
            {
                _hackers.Remove(ev.Player.UserId);
                _guards.Remove(ev.Player.UserId);
                _kittedPlayers.Remove(ev.Player.UserId);
                CustomUnits.RemoveMember(ev.Player);

                // Если хакер выбыл во время взлома — сброс
                if (_hackers.Contains(ev.Player.UserId))
                    ResetPanelHack("хакер выбыл");
            }
        }

        _spawnProtection.Remove(ev.Player.UserId);
    }

    private void OnDied(DiedEventArgs ev)
    {
        if (ev.Player == null) return;

        if (_squadMembers.Remove(ev.Player.UserId))
        {
            bool wasHacker = _hackers.Remove(ev.Player.UserId);
            _guards.Remove(ev.Player.UserId);
            _kittedPlayers.Remove(ev.Player.UserId);
            CustomUnits.RemoveMember(ev.Player);

            if (wasHacker)
                ResetPanelHack("хакер погиб");
        }

        _spawnProtection.Remove(ev.Player.UserId);
    }

    private void OnLeft(LeftEventArgs ev)
    {
        if (ev.Player == null) return;
        _squadMembers.Remove(ev.Player.UserId);
        _hackers.Remove(ev.Player.UserId);
        _guards.Remove(ev.Player.UserId);
        _kittedPlayers.Remove(ev.Player.UserId);
        _spawnProtection.Remove(ev.Player.UserId);
    }

    private void OnHurting(HurtingEventArgs ev)
    {
        if (ev.Player == null) return;

        if (_spawnProtection.TryGetValue(ev.Player.UserId, out var until) &&
            DateTime.UtcNow < until &&
            ev.DamageHandler.Type != DamageType.Warhead)
        {
            ev.IsAllowed = false;
        }
    }

    private void OnInteractingDoor(InteractingDoorEventArgs ev)
    {
        // Блокируем лифты во время Omega
        if (_omegaActive && ev.Door.Type is DoorType.ElevatorGateA or DoorType.ElevatorGateB)
            ev.IsAllowed = false;
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

    private string? _activeHackPanelId;
    private int _activeHackProgress;
    private string? _panelPosition_legacy;
    
    private void StartPanelHack(Player player, HackPanel panel)
    {
        if (_panelPosition == null && panel.WorldPos == null)
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

        int hackedCount = _panels.Count(p => p.Hacked);
        player.ShowZoneHint(HintZone.Notification,
            $"<color=#a78bfa>💻 Взлом панели «{panel.DisplayName}»...\n" +
            $"<size=70%>Прогресс: {hackedCount}/4 панелей взломано</color></size>", 3f, "hackers", 22);

        Timing.RunCoroutine(PanelHackCoroutine(player, panel), $"{HackTag}{panel.Id}");
    }

    private IEnumerator<float> PanelHackCoroutine(Player hacker, HackPanel panel)
    {
        float tick = Mathf.Max(0.5f, _config.HackTickSeconds);
        int progress = 0;
        var panelPos = panel.WorldPos ?? Vector3.zero;

        SetScreensColorForPanel(panel, new Color32(167, 139, 250, 255));

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

            if (progress % 10 == 0)
            {
                hacker.ShowZoneHint(HintZone.LowerRight,
                    $"<color=#a78bfa>💻 Взлом «{panel.DisplayName}»: <b>{progress * 100 / 30}%</b></color>",
                    1.2f, "hackers_progress", 24);
            }
        }

        // Панель взломана!
        panel.Hacked = true;
        SetScreensColorForPanel(panel, new Color32(163, 230, 53, 255));

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
        Timing.KillCoroutines("hackers_hack");
        _hackers.Clear();
        _activeHackPanelId = null;

        if (!string.IsNullOrEmpty(reason))
            BroadcastToAll($"<color=#f87171>💥 Взлом прерван ({reason})!</color>");
    }

    private void SetScreensColorForPanel(HackPanel panel, Color32 color)
    {
        // Экраны из JSON-схематики статичны — цвет уже задан в JSON
        // Динамическая смена цвета для схематик не поддерживается
    }

    // ------------------------------------------------------------------
    //  Все панели взломаны → Комната управления
    // ------------------------------------------------------------------

    private void AllPanelsHacked()
    {
        BuildControlRoom();

        BroadcastToAll(
            "<color=#a78bfa><b>💻 ВСЕ 4 ПАНЕЛИ ВЗЛОМАНЫ</b></color>\n" +
            "<size=70%><color=#ef4444>Комната Управления разблокирована! Найдите её и завершите взлом.\n" +
            "Это запустит OMEGA WARHEAD.</color></size>");

        try
        {
            Exiled.API.Features.Cassie.Message(
                "ATTENTION . ALL SERVER SYSTEMS COMPROMISED . PROTOCOL OMEGA READY", false, false, false);
        }
        catch { }
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

        Timing.RunCoroutine(ControlHackCoroutine(player), $"{HackTag}control");
    }

    private IEnumerator<float> ControlHackCoroutine(Player hacker)
    {
        var panelPos = _controlRoom?.WorldPos ?? Vector3.zero;
        int progress = 0;
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

            if (progress % 10 == 0)
            {
                hacker.ShowZoneHint(HintZone.LowerRight,
                    $"<color=#a78bfa>💻 Комната Управления: <b>{progress}%</b></color>", 1.2f, "hackers_ctrl", 24);
            }

            if (progress == 50)
            {
                AlertFacility();
            }
        }

        CompleteControlHack(hacker);
    }

    private void ResetControlHack(string reason)
    {
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
    //  Omega Warhead
    // ------------------------------------------------------------------

    private IEnumerator<float> OmegaSequence()
    {
        _omegaActive = true;
        float elapsed = 0f;
        bool pulseBright = false;

        SnapshotLights();

        // Блокируем лифты
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

        while (elapsed < _config.OmegaCountdownSeconds)
        {
            yield return Timing.WaitForSeconds(1f);
            elapsed += 1f;
            pulseBright = !pulseBright;

            SetAllRoomsColor(pulseBright
                ? new Color32(120, 10, 10, 255)
                : new Color32(35, 5, 5, 255));

            int remaining = (int)(_config.OmegaCountdownSeconds - elapsed);

            if (remaining > 0 && remaining % 30 == 0)
            {
                BroadcastToAll($"<color=#ef4444><b>☢ OMEGA WARHEAD:</b> до детонации {remaining / 60}:{remaining % 60:D2}</color>");
            }
        }

        // Детонация
        foreach (var pl in Player.List.Where(x => x is { IsAlive: true }).ToList())
        {
            try
            {
                Map.ExplodeEffect(pl.Position, ProjectileType.FragGrenade);
                pl.Health = 0f;
            }
            catch { }
        }

        yield return Timing.WaitForSeconds(5f);

        try { Round.Restart(); } catch { }
    }

    // ------------------------------------------------------------------
    //  Свет / утилиты
    // ------------------------------------------------------------------

    private void SnapshotLights()
    {
        _originalRoomColors.Clear();
        foreach (var room in Room.List)
        {
            try { _originalRoomColors[room] = room.Color; } catch { }
        }
        _lightsModified = true;
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
