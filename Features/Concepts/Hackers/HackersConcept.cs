using System;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.ServerSpecific;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Server;
using Exiled.Events.EventArgs.Player;
using Exiled.API.Features.Doors;
using Exiled.API.Features.Toys;
using PlayerRoles;
using MEC;
using Mirror;
using UnityEngine;
using Light = Exiled.API.Features.Toys.Light;

namespace Capy.NoRules.Features.Concepts.Hackers;

/// <summary>
/// Концепт «Хакеры»:
/// Отряд хакеров проникает в серверную комнату комплекса и пытается взломать
/// панель управления. Взлом требует 75 тиков (секунд) непрерывной работы —
/// если хакер отходит дальше радиуса, прогресс сбрасывается.
/// После успешного взлома запускается последовательность Omega Warhead:
/// красное мигание света по всему комплексу, обратный отсчёт, и в конце
/// детонация, убивающая всех живых.
/// </summary>
public sealed class HackersConcept
{
    private readonly HackersConfig _config;

    private readonly HashSet<string> _squadMembers = new();
    private readonly HashSet<string> _kittedPlayers = new();
    private readonly HashSet<string> _hackers = new();
    private readonly Dictionary<string, DateTime> _spawnProtection = new();
    private readonly List<GameObject> _spawnedObjects = new();
    private readonly List<Light> _glowLights = new();

    private CoroutineHandle _hackLoop;
    private CoroutineHandle _omegaLoop;
    private readonly Dictionary<Exiled.API.Features.Room, Color32> _originalRoomColors = new();
    private bool _lightsModified;
    private bool _enabled;
    private int _hackProgress;
    private bool _spawnedThisRound;

    // Панель взлома
    private Vector3? _panelPosition;
    private Primitive? _panelCore;
    private Primitive? _panelScreen1;
    private Primitive? _panelScreen2;
    private Light? _panelGlow;

    public HackersConcept(HackersConfig config)
    {
        _config = config;
    }

    public bool IsActive { get; private set; }
    public bool IsHacking => _hackProgress > 0 && _hackProgress < _config.HackTicksRequired;

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
        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
        Exiled.Events.Handlers.Player.Spawned -= OnSpawned;
        Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        Exiled.Events.Handlers.Player.Left -= OnLeft;
        Exiled.Events.Handlers.Player.Hurting -= OnHurting;
        AssKeybinds.OnKeybindPressed -= OnKeybindPressed;

        Teardown();
    }

    private void OnWaitingForPlayers() => ResetState();

    private void ResetState()
    {
        Timing.KillCoroutines("hackers_hack");
        Timing.KillCoroutines("hackers_omega");

        RestoreLights();
        Teardown();

        _squadMembers.Clear();
        _kittedPlayers.Clear();
        _hackers.Clear();
        _spawnProtection.Clear();
        _hackProgress = 0;
        IsActive = false;
        _panelPosition = null;
    }

    private void OnRestartingRound() => ResetState();

    // ------------------------------------------------------------------
    //  Построение серверной комнаты (панели + стойки + свечение)
    // ------------------------------------------------------------------

    private void OnRoundStarted()
    {
        try
        {
            var roomType = Enum.TryParse<RoomType>(_config.PanelRoom, ignoreCase: true, out var parsed)
                ? parsed : RoomType.HczServerRoom;

            var room = Room.List.FirstOrDefault(r => r.Type == roomType);
            if (room == null)
            {
                Log.Warn($"[Hackers] Комната {_config.PanelRoom} не найдена — панели не построены.");
                return;
            }

            Vector3 root = room.Position + room.Rotation *
                new Vector3(_config.OffsetX, _config.OffsetY, _config.OffsetZ);
            Quaternion rot = room.Rotation;

            BuildServerRoom(root, rot);

            _panelPosition = root + rot * new Vector3(0f, 0f, -0.6f);

            StationsManager.Register(
                "hackers_panel",
                _panelPosition.Value,
                Mathf.Max(1.5f, _config.PanelRadius),
                p => StartHack(p));

            Log.Debug($"[Hackers] Панель построена: {_panelPosition.Value}");
        }
        catch (Exception ex)
        {
            Log.Error($"[Hackers] Ошибка построения: {ex}");
        }
    }

    private void BuildServerRoom(Vector3 root, Quaternion rot)
    {
        // Серверные стойки (тёмные шкафы с цветными огоньками)
        for (int i = -1; i <= 1; i++)
        {
            var rack = Primitive.Create(
                PrimitiveType.Cube,
                AdminToys.PrimitiveFlags.Visible,
                root + rot * new Vector3(i * 0.9f, 0f, -0.9f),
                rot.eulerAngles,
                new Vector3(0.7f, 2.1f, 0.45f),
                spawn: true,
                color: new Color32(18, 20, 24, 255));

            if (rack != null) Track(rack.GameObject);

            // Огоньки на стойке (HDR-свечение через Light)
            var rackLight = Light.Create(
                position: root + rot * new Vector3(i * 0.9f, 1.4f, -0.55f),
                rotation: null,
                scale: Vector3.one,
                spawn: false,
                color: i == 0 ? new Color32(255, 80, 80, 255) : new Color32(80, 200, 120, 255));

            if (rackLight != null)
            {
                rackLight.Intensity = 2f;
                rackLight.Range = 1.2f;
                rackLight.Spawn();
                Track(rackLight.GameObject);
                _glowLights.Add(rackLight);
            }
        }

        // Главная панель взлома
        var panelBody = Primitive.Create(
            PrimitiveType.Cube,
            AdminToys.PrimitiveFlags.Visible,
            root,
            rot.eulerAngles,
            new Vector3(1.1f, 0.85f, 0.14f),
            spawn: true,
            color: new Color32(28, 32, 40, 255));

        if (panelBody != null) Track(panelBody.GameObject);

        // Экраны панели (тёмные — «выключены», загораются при взломе)
        _panelScreen1 = Primitive.Create(
            PrimitiveType.Cube,
            AdminToys.PrimitiveFlags.Visible,
            root + rot * new Vector3(-0.26f, 0.12f, -0.08f),
            rot.eulerAngles,
            new Vector3(0.42f, 0.42f, 0.02f),
            spawn: true,
            color: new Color32(8, 10, 12, 255));

        _panelScreen2 = Primitive.Create(
            PrimitiveType.Cube,
            AdminToys.PrimitiveFlags.Visible,
            root + rot * new Vector3(0.26f, 0.12f, -0.08f),
            rot.eulerAngles,
            new Vector3(0.42f, 0.42f, 0.02f),
            spawn: true,
            color: new Color32(8, 10, 12, 255));

        if (_panelScreen1 != null) Track(_panelScreen1.GameObject);
        if (_panelScreen2 != null) Track(_panelScreen2.GameObject);

        // Красная кнопка запуска
        var button = Primitive.Create(
            PrimitiveType.Cylinder,
            AdminToys.PrimitiveFlags.Visible | AdminToys.PrimitiveFlags.Collidable,
            root + rot * new Vector3(0f, -0.28f, -0.09f),
            rot.eulerAngles,
            new Vector3(0.08f, 0.03f, 0.08f),
            spawn: true,
            color: new Color32(220, 40, 40, 255));

        if (button != null) Track(button.GameObject);

        // HDR-подсветка панели (всегда включена — привлекает внимание к точке взлома)
        _panelGlow = Light.Create(
            position: root + rot * new Vector3(0f, 0.3f, 0.35f),
            rotation: null,
            scale: Vector3.one,
            spawn: false,
            color: new Color32(255, 60, 60, 255));

        if (_panelGlow != null)
        {
            _panelGlow.Intensity = 3f;
            _panelGlow.Range = 2.5f;
            _panelGlow.Spawn();
            Track(_panelGlow.GameObject);
            _glowLights.Add(_panelGlow);
        }
    }

    private void Track(GameObject go) => _spawnedObjects.Add(go);

    private void SetScreensColor(Color32 color)
    {
        try
        {
            if (_panelScreen1 != null) _panelScreen1.Color = color;
            if (_panelScreen2 != null) _panelScreen2.Color = color;
        }
        catch { }
    }

    private static bool IsScpRole(RoleTypeId role)
        => role is RoleTypeId.Scp049 or RoleTypeId.Scp0492 or RoleTypeId.Scp079 or RoleTypeId.Scp096
            or RoleTypeId.Scp106 or RoleTypeId.Scp173 or RoleTypeId.Scp3114 or RoleTypeId.Scp939;

    private void Teardown()
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
        _glowLights.Clear();
        _panelCore = null;
        _panelScreen1 = null;
        _panelScreen2 = null;
        _panelGlow = null;
        _panelPosition = null;
        StationsManager.Unregister("hackers_panel");
    }

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

    // ------------------------------------------------------------------
    //  Формирование отряда хакеров
    // ------------------------------------------------------------------

    /// <summary>
    /// Попытка конвертировать волну в Отряд Хакеров (если CO2 не взял эту волну).
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

    private bool CanAutoSpawn()
    {
        if (UnityEngine.Random.Range(0, 100) >= Mathf.Clamp(_config.ChancePercent, 0, 100))
            return false;

        if (_panelPosition == null) return false;

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

        // Свой шафл
        for (int i = spectators.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (spectators[i], spectators[j]) = (spectators[j], spectators[i]);
        }

        return spectators.Take(max).ToList();
    }

    private void RegisterSquad(List<Player> squad)
    {
        _spawnedThisRound = true;

        int index = 0;
        foreach (var member in squad)
        {
            index++;
            _squadMembers.Add(member.UserId);
            _kittedPlayers.Remove(member.UserId);

            string rank = index switch
            {
                1 => "Лид хакеров",
                _ => "Хакер"
            };

            CustomUnits.AssignMember(member, "Группировка «Хакеры»", "#a78bfa", rank);

            _spawnProtection[member.UserId] = DateTime.UtcNow.AddSeconds(10f);

            member.ShowZoneHint(HintZone.TopCenter,
                $"<color=#a78bfa><b>Вы — {rank} группировки «Хакеры»</b></color>\n" +
                "<size=70%><color=#c2c2c2>Миссия: найти серверную комнату и удерживать [E] на панели\n" +
                "до завершения взлома. Не отходите от панели — прогресс сбросится!</color></size>",
                10f, "hackers_brief", 20);
        }

        BroadcastToAll("<color=#a78bfa>🕶 В комплекс проникла <b>группировка «Хакеры»</b>...</color>");
    }

    private void ApplyKit(Player player)
    {
        try
        {
            player.ClearInventory();
            player.AddItem(ItemType.KeycardFacilityManager);
            player.AddItem(ItemType.GunCOM18);
            player.AddItem(ItemType.SCP268); // шляпа-невидимость для стелса
            player.AddItem(ItemType.Flashlight);
            player.AddItem(ItemType.Radio);
            player.AddItem(ItemType.ArmorCombat);
        }
        catch (Exception ex)
        {
            Log.Debug($"[Hackers] ApplyKit: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    //  Членство / защита
    // ------------------------------------------------------------------

    private bool IsSquadMember(Player? player)
        => player != null && _squadMembers.Contains(player.UserId);

    private void OnSpawned(SpawnedEventArgs ev)
    {
        if (ev.Player == null || !_squadMembers.Contains(ev.Player.UserId)) return;
        if (_kittedPlayers.Contains(ev.Player.UserId)) return;

        _kittedPlayers.Add(ev.Player.UserId);
        Timing.CallDelayed(0.5f, () =>
        {
            if (ev.Player != null && ev.Player.IsConnected && _squadMembers.Contains(ev.Player.UserId))
                ApplyKit(ev.Player);
        });
    }

    private void OnChangingRole(ChangingRoleEventArgs ev)
    {
        if (ev.Player == null) return;

        // Хакер перестал быть живой человеческой ролью
        if (ev.NewRole is RoleTypeId.None or RoleTypeId.Spectator or RoleTypeId.Overwatch ||
            IsScpRole(ev.NewRole))
        {
            if (_squadMembers.Remove(ev.Player.UserId))
            {
                _kittedPlayers.Remove(ev.Player.UserId);
                _hackers.Remove(ev.Player.UserId);
                CustomUnits.RemoveMember(ev.Player);

                // Активный хакер отвалился — взлом сбрасывается
                if (_hackers.Contains(ev.Player.UserId))
                    ResetHack("хакер выбыл");
            }
        }

        // Защита спавна снимается при смене роли
        _spawnProtection.Remove(ev.Player.UserId);
    }

    private void OnDied(DiedEventArgs ev)
    {
        if (ev.Player == null) return;

        if (_squadMembers.Remove(ev.Player.UserId))
        {
            _kittedPlayers.Remove(ev.Player.UserId);
            _hackers.Remove(ev.Player.UserId);
            CustomUnits.RemoveMember(ev.Player);

            if (_hackers.Contains(ev.Player.UserId))
                ResetHack("хакер погиб");
        }

        _spawnProtection.Remove(ev.Player.UserId);
    }

    private void OnLeft(LeftEventArgs ev)
    {
        if (ev.Player == null) return;

        _squadMembers.Remove(ev.Player.UserId);
        _kittedPlayers.Remove(ev.Player.UserId);
        _hackers.Remove(ev.Player.UserId);
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

    // ------------------------------------------------------------------
    //  Взлом
    // ------------------------------------------------------------------

    private void OnKeybindPressed(Player player, CustomKeybind keybind)
    {
        if (keybind != CustomKeybind.E)
            return;

        StationsManager.TryInvokeNearest(player);
    }

    private void StartHack(Player player)
    {
        if (_panelPosition == null || player == null || !player.IsAlive)
            return;

        if (IsActive)
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#94a3b8>Системы Omega уже скомпрометированы.</color>", 2f, "hackers", 20);
            return;
        }

        if (!IsSquadMember(player))
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#94a3b8>Панель защищена. Требуется доступ группировки «Хакеры».</color>", 2f, "hackers", 20);
            return;
        }

        // Уже идёт взлом этим или другим хакером
        if (_hackers.Count > 0)
            return;

        Timing.RunCoroutine(HackCoroutine(player), "hackers_hack");
    }

    private IEnumerator<float> HackCoroutine(Player hacker)
    {
        _hackers.Add(hacker.UserId);
        _hackProgress = 0;

        hacker.ShowZoneHint(HintZone.Notification,
            "<color=#a78bfa>💻 <b>Взлом начат.</b> Не отходите от панели!</color>", 3f, "hackers", 22);

        // Экраны загораются фиолетовым
        SetScreensColor(new Color32(167, 139, 250, 255));
        SetPanelGlow(new Color32(167, 139, 250, 255));

        float tickSeconds = Mathf.Max(0.5f, _config.HackTickSeconds);

        while (_hackProgress < _config.HackTicksRequired)
        {
            yield return Timing.WaitForSeconds(tickSeconds);

            if (!hacker.IsConnected || !hacker.IsAlive)
            {
                ResetHack("хакер выбыл");
                yield break;
            }

            if (_panelPosition == null ||
                (hacker.Position - _panelPosition.Value).sqrMagnitude >
                _config.HackRadius * _config.HackRadius)
            {
                ResetHack("хакер отошёл от панели");
                yield break;
            }

            _hackProgress++;

            if (_hackProgress % 10 == 0)
            {
                hacker.ShowZoneHint(HintZone.LowerRight,
                    $"<color=#a78bfa>💻 Взлом: <b>{_hackProgress}%</b></color>", 1.2f, "hackers_progress", 24);

                // Мигание экранов
                bool bright = _hackProgress % 20 == 0;
                SetScreensColor(bright
                    ? new Color32(196, 181, 253, 255)
                    : new Color32(124, 92, 191, 255));
            }

            if (_hackProgress == 50)
            {
                // Тревога на середине взлома
                AlertFacility();
            }
        }

        CompleteHack(hacker);
    }

    private void ResetHack(string reason)
    {
        Timing.KillCoroutines("hackers_hack");

        if (_hackProgress > 0)
            BroadcastToAll($"<color=#f87171>💥 Взлом сорван ({reason})!</color>");

        _hackers.Clear();
        _hackProgress = 0;
        SetScreensColor(new Color32(8, 10, 12, 255));
        SetPanelGlow(new Color32(255, 60, 60, 255));
    }

    private void AlertFacility()
    {
        BroadcastToAll("<color=red><b>⚠ ВНИМАНИЕ ВСЕМУ ПЕРСОНАЛУ</b></color>\n" +
            "<size=70%><color=#6f6f6f>Замечено хакерское вторжение в системы комплекса.\n" +
            "Требуется немедленная реакция средств самообороны!</color></size>");

        try
        {
            Exiled.API.Features.Cassie.Message(_config.CassieAlert, false, false, false);
        }
        catch { }
    }

    private void CompleteHack(Player hacker)
    {
        IsActive = true;
        _hackers.Clear();
        SetScreensColor(new Color32(163, 230, 53, 255));
        SetPanelGlow(new Color32(163, 230, 53, 255));

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

        Timing.RunCoroutine(OmegaSequence(), "hackers_omega");
    }

    private IEnumerator<float> OmegaSequence()
    {
        float elapsed = 0f;
        bool pulseBright = false;

        SnapshotLights();

        // Красное пульсирующее освещение весь отсчёт
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
                BroadcastToAll($"<color=#ef4444><b>OMEGA WARHEAD:</b> до детонации {remaining / 60}:{remaining % 60:D2}</color>");
            }
        }

        // Детонация: убиваем всё живое эффектными взрывами
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

    private void SetPanelGlow(Color32 color)
    {
        try
        {
            if (_panelGlow != null)
                _panelGlow.Color = color;
        }
        catch { }
    }

    private void BroadcastToAll(string message)
    {
        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, message, 6f, "hackers", 22);
    }
}
