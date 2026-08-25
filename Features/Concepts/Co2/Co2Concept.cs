using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.Studio.Core;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Capy.Engine.ServerSpecific;
using PlayerRoles;
using Exiled.API.Features.Doors;
using Exiled.API.Features.Toys;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Server;
using MapGeneration.Distributors;
using MEC;
using Mirror;
using UnityEngine;

namespace Capy.NoRules.Features.Concepts.Co2;

/// <summary>
/// Концепт «Отряд CO2»:
/// Вместо обычной волны МОГ приезждает специализированный отряд с миссией —
/// активировать затопление комплекса угарным газом на двух станциях в HID-комнате.
/// Активация требует одновременного удержания обеих панелей; не-отряд игроки
/// могут сорвать активацию или деактивировать уже запущенный CO2 на тех же станциях.
/// Через ~3.5 минуты после активации весь комплекс (кроме SCP) начинает получать
/// урон от отравления, лифты блокируются, раунд завершается.
///
/// Порт концепта Loli.Concepts.CO2 (Fydne) на EXILED/CapyLib.
/// </summary>
public sealed class Co2Concept
{
    private readonly Co2Config _config;

    // --- состояние ---
    private bool _spawnedThisRound;
    private bool _activated;
    private bool _allowCancel = true;
    private bool _door1Active;
    private bool _door2Active;
    private readonly HashSet<string> _squadMembers = new();
    private readonly HashSet<string> _kittedPlayers = new();
    private readonly HashSet<string> _activators = new();
    private readonly Dictionary<string, DateTime> _spawnProtection = new();
    // Флаг: ближайшее имя юнита волны заменить на «Отряд СО₂»
    private bool _pendingSquadWave;
    private DateTime _roundStartTime = DateTime.UtcNow;

    // --- визуал HID ---
    private Primitive? _indicator1;
    private Primitive? _indicator2;
    private readonly List<GameObject> _spawnedObjects = new();
    private readonly List<Door> _lockedDoors = new();
    private readonly Dictionary<Room, Color32> _originalRoomColors = new();

    private static readonly Color32 IndicatorIdle = new(161, 157, 148, 255);
    private static readonly Color32 IndicatorActive = new(20, 177, 224, 255);
    private static readonly Color32 FrameColor = new(161, 157, 148, 255);

    private const string ProcessTag = "co2_process";
    private const string PoisonTag = "co2_poison";
    private const string AudioKey = "CapyCo2Audio";

    public Co2Concept(Co2Config config)
    {
        _config = config;
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

        Exiled.Events.Handlers.Server.AddingUnitName += OnAddingUnitName;
        Exiled.Events.Handlers.Player.Spawned += OnSpawned;
        Exiled.Events.Handlers.Player.ChangingRole += OnChangingRole;
        Exiled.Events.Handlers.Player.Died += OnDied;
        Exiled.Events.Handlers.Player.Left += OnLeft;
        Exiled.Events.Handlers.Player.Hurting += OnHurting;
        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
    }

    public void Disable()
    {
        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;

        Exiled.Events.Handlers.Server.AddingUnitName -= OnAddingUnitName;
        Exiled.Events.Handlers.Player.Spawned -= OnSpawned;
        Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        Exiled.Events.Handlers.Player.Left -= OnLeft;
        Exiled.Events.Handlers.Player.Hurting -= OnHurting;
        AssKeybinds.OnKeybindPressed -= OnKeybindPressed;

        TeardownVisuals();
        ConceptsController.Disable();
    }

    public void OnWaitingForPlayers()
    {
        ResetState();
        _pendingSquadWave = false;
    }

    private void OnAddingUnitName(AddingUnitNameEventArgs ev)
    {
        // Имя юнита остаётся ванильным — члены отряда помечены через CustomInfo
        _pendingSquadWave = false;
    }

    private void OnRestartingRound()
    {
        Timing.KillCoroutines(ProcessTag);
        Timing.KillCoroutines(PoisonTag);

        RestoreLights();
        UnlockElevators();

        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ClearCapyHint("co2");

        TeardownVisuals();

        _spawnedThisRound = false;
        _activated = false;
        _allowCancel = true;
        _door1Active = false;
        _door2Active = false;
        _squadMembers.Clear();
        _kittedPlayers.Clear();
        _activators.Clear();
        _spawnProtection.Clear();

        CustomUnits.Clear();
        StationsManager.Unregister("co2_panel1");
        StationsManager.Unregister("co2_panel2");
        ConceptsController.Disable();
    }

    private void ResetState() => OnRestartingRound();

    // ------------------------------------------------------------------
    //  Построение панелей в HID
    // ------------------------------------------------------------------

    private void OnRoundStarted()
    {
        _roundStartTime = DateTime.UtcNow;

        try
        {
            var room = Room.List.FirstOrDefault(r => r.Type == RoomType.HczHid);
            if (room == null)
            {
                Log.Warn("[CO2] Комната HczHid не найдена — панели не построены.");
                return;
            }

            Vector3 rootPos = room.Position + room.Rotation *
                new Vector3(_config.OffsetX, _config.OffsetY, _config.OffsetZ);
            Quaternion rootRot = room.Rotation;

            BuildPanel(rootPos, rootRot, left: true);
            BuildPanel(rootPos, rootRot, left: false);

            var station1 = StationsManager.Register(
                "co2_panel1",
                PanelWorldPos(rootPos, rootRot, -1.3f),
                Mathf.Max(1.5f, _config.PanelRadius),
                p => Interact(p, isPanel1: true));

            StationsManager.Register(
                "co2_panel2",
                PanelWorldPos(rootPos, rootRot, 1.3f),
                Mathf.Max(1.5f, _config.PanelRadius),
                p => Interact(p, isPanel1: false));

            Log.Debug($"[CO2] Панели построены в {room.Type}. Панель1: {station1.Position}.");
        }
        catch (Exception ex)
        {
            Log.Error($"[CO2] Ошибка построения панелей: {ex}");
        }
    }

    private Vector3 PanelWorldPos(Vector3 rootPos, Quaternion rootRot, float xOffset)
        => rootPos + rootRot * new Vector3(xOffset, 0f, 0.075f);

    private void BuildPanel(Vector3 rootPos, Quaternion rootRot, bool left)
    {
        float side = left ? -1.3f : 1.3f;
        Vector3 framePos = rootPos + rootRot * new Vector3(side, 0f, 0f);

        // Рамка панели (декор)
        var frame = Primitive.Create(
            primitiveType: PrimitiveType.Cube,
            flags: AdminToys.PrimitiveFlags.Visible,
            position: framePos,
            rotation: rootRot.eulerAngles,
            scale: new Vector3(0.3f, 0.2f, 0.1f),
            spawn: true,
            color: FrameColor);

        if (frame != null)
            Track(frame.GameObject);

        // Индикатор состояния (меняет цвет серый ↔ синий)
        var indicator = Primitive.Create(
            primitiveType: PrimitiveType.Cube,
            flags: AdminToys.PrimitiveFlags.Visible,
            position: PanelWorldPos(rootPos, rootRot, side),
            rotation: rootRot.eulerAngles,
            scale: new Vector3(0.5f, 0.277f, 0.1f),
            spawn: true,
            color: IndicatorIdle);

        if (indicator != null)
        {
            if (left) _indicator1 = indicator;
            else _indicator2 = indicator;

            Track(indicator.GameObject);
        }

        // Настоящая воркстейшн для антуража и взаимодействия взглядом
        try
        {
            if (PrefabManager.WorkstationPrefab != null)
            {
                var wsGo = UnityEngine.Object.Instantiate(
                    PrefabManager.WorkstationPrefab.gameObject,
                    framePos + rootRot * new Vector3(0f, -0.35f, 0.05f),
                    rootRot * Quaternion.Euler(0f, 180f, 0f));

                wsGo.transform.localScale = new Vector3(0.193f, 0.232f, 0.06f);
                NetworkServer.Spawn(wsGo);
                Track(wsGo);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[CO2] Воркстейшн не заспавнен: {ex.Message}");
        }
    }

    private void Track(GameObject go)
    {
        _spawnedObjects.Add(go);
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
        _indicator1 = null;
        _indicator2 = null;
    }

    private void SetIndicator(bool panel1, Color32 color)
    {
        try
        {
            var prim = panel1 ? _indicator1 : _indicator2;
            if (prim != null)
                prim.Color = color;
        }
        catch { }
    }

    // ------------------------------------------------------------------
    //  Формирование отряда
    // ------------------------------------------------------------------

    /// <summary>Условия автоспавна отряда выполнены?</summary>
    private bool CanAutoSpawn()
    {
        if (_spawnedThisRound) return false;
        if (UnityEngine.Random.Range(0, 100) >= Mathf.Clamp(_config.ChancePercent, 0, 100)) return false;
        if (_config.RequireEscapedScientists && Round.EscapedScientists <= 0) return false;
        if (Player.List.Any(p => p is { IsAlive: true, Role.Type: RoleTypeId.Scientist })) return false;

        return true;
    }

    private List<Player>? CollectCandidates(int max)
    {
        var spectators = Player.List
            .Where(p => p != null && p.IsConnected && !p.IsNPC &&
                        (p.Role.Type == RoleTypeId.Spectator || p.Role.Type == RoleTypeId.Overwatch))
            .ToList();

        if (spectators.Count < Math.Max(1, _config.MinSpectators))
            return null;

        for (int i = spectators.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (spectators[i], spectators[j]) = (spectators[j], spectators[i]); }
        return spectators.Take(max).ToList();
    }

    /// <summary>
    /// Попытка конвертировать волну МОГ в Отряд СО2. false — условия не выполнены.
    /// </summary>
    public bool TryTakeWave(RespawningTeamEventArgs ev)
    {
        if (!_config.IsEnabled || _spawnedThisRound || !CanAutoSpawn())
            return false;

        var squad = CollectCandidates(Math.Min(_config.SquadSizeMax, ev.MaximumRespawnAmount));
        if (squad == null || squad.Count == 0)
            return false;

        ev.Players.Clear();
        ev.Players.AddRange(squad);

        // Имя юнита подменяем через AddingUnitName (флаг _pendingSquadWave)
        _pendingSquadWave = true;

        RegisterSquad(squad);
        return true;
    }

    /// <summary>Принудительный вызов отряда админ-командой.</summary>
    public string ForceSpawnFromSpectators()
    {
        if (!_config.IsEnabled)
            return "Концепт CO2 выключен.";

        if (_spawnedThisRound)
            return "Отряд СО2 уже приезжал в этом раунде.";

        var squad = CollectCandidates(_config.SquadSizeMax);
        if (squad == null)
            return $"Недостаточно спектаторов (нужно минимум {_config.MinSpectators}).";

        foreach (var member in squad)
        {
            member.Role.Set(PlayerRoles.RoleTypeId.NtfSpecialist, Exiled.API.Enums.SpawnReason.None);
        }

        Timing.CallDelayed(0.6f, () => RegisterSquad(squad));
        return $"Отряд СО2 вызван ({squad.Count} чел.).";
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
                1 => "Капитан",
                < 8 => "Специалист",
                _ => "Кадет"
            };

            CustomUnits.AssignMember(member, "Отряд СО2", "#14b1e0", rank);
            ApplyKit(member, index);

            _spawnProtection[member.UserId] = DateTime.UtcNow.AddSeconds(10f);

            member.ShowZoneHint(HintZone.TopCenter,
                $"<color=#14b1e0><b>Вы — {rank} отряда активации <color=#0099ff>СО₂</color> МОГ</b></color>\n" +
                "<size=70%><color=#c2c2c2>Миссия: обеспечить активацию CO2 на двух панелях в HID-комнате.\n" +
                "[E] рядом с панелью. Держитесь рядом во время активации!</color></size>",
                10f, "co2_brief", 20);
        }

        BroadcastToAll($"<color=#14b1e0>🚁 На комплекс прибыл <b>Отряд СО₂</b> МОГ ({squad.Count} чел.)</color>");
    }

    private void ApplyKit(Player player, int index)
    {
        try
        {
            player.ClearInventory();

            switch (index)
            {
                case 1: // Капитан
                    player.AddItem(ItemType.KeycardMTFCaptain);
                    player.AddItem(ItemType.GunE11SR);
                    player.AddItem(ItemType.ParticleDisruptor);
                    player.AddItem(ItemType.SCP500);
                    player.AddItem(ItemType.Adrenaline);
                    player.AddItem(ItemType.AntiSCP207);
                    player.AddItem(ItemType.Radio);
                    player.AddItem(ItemType.ArmorCombat);
                    break;

                default: // Специалист / Кадет
                    player.AddItem(index < 8 ? ItemType.KeycardMTFOperative : ItemType.KeycardMTFPrivate);
                    player.AddItem(ItemType.GunE11SR);
                    player.AddItem(ItemType.SCP500);
                    player.AddItem(ItemType.Medkit);
                    player.AddItem(ItemType.Adrenaline);
                    player.AddItem(ItemType.AntiSCP207);
                    player.AddItem(ItemType.Radio);
                    player.AddItem(ItemType.ArmorCombat);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[CO2] ApplyKit: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    //  Членство / защита спавна
    // ------------------------------------------------------------------

    private bool IsSquadMember(Player? player)
        => player != null && _squadMembers.Contains(player.UserId);

    private void OnSpawned(SpawnedEventArgs ev)
    {
        if (ev.Player == null || !_squadMembers.Contains(ev.Player.UserId))
            return;

        if (_kittedPlayers.Contains(ev.Player.UserId))
            return;

        _kittedPlayers.Add(ev.Player.UserId);
        Timing.CallDelayed(0.5f, () =>
        {
            if (ev.Player != null && ev.Player.IsConnected && _squadMembers.Contains(ev.Player.UserId))
                ApplyKit(ev.Player, GetRankIndex(ev.Player.UserId));
        });
    }

    private int GetRankIndex(string userId)
    {
        // Порядок выдачи совпадает с порядком регистрации
        int i = 0;
        foreach (var id in _squadMembers)
        {
            i++;
            if (id == userId) return i;
        }

        return 99;
    }

    private void OnChangingRole(ChangingRoleEventArgs ev)
    {
        if (ev.Player == null) return;

        // Игрок вышел из отряда (умер/сменен) — чистим членство
        if (!IsNtfRole(ev.NewRole) && _squadMembers.Contains(ev.Player.UserId))
        {
            _squadMembers.Remove(ev.Player.UserId);
            _kittedPlayers.Remove(ev.Player.UserId);
            CustomUnits.RemoveMember(ev.Player);
        }
    }

    private void OnDied(DiedEventArgs ev)
    {
        if (ev.Player == null) return;

        if (_squadMembers.Contains(ev.Player.UserId))
        {
            _squadMembers.Remove(ev.Player.UserId);
            CustomUnits.RemoveMember(ev.Player);
        }

        _spawnProtection.Remove(ev.Player.UserId);
        _activators.Remove(ev.Player.UserId);
    }

    private void OnLeft(LeftEventArgs ev)
    {
        if (ev.Player == null) return;

        _squadMembers.Remove(ev.Player.UserId);
        _kittedPlayers.Remove(ev.Player.UserId);
        _spawnProtection.Remove(ev.Player.UserId);
        _activators.Remove(ev.Player.UserId);
    }

    private void OnHurting(HurtingEventArgs ev)
    {
        if (ev.Player == null) return;

        // Защита отряда первые N секунд после спавна (кроме вархеда)
        if (_spawnProtection.TryGetValue(ev.Player.UserId, out var until) &&
            DateTime.UtcNow < until &&
            ev.DamageHandler.Type != Exiled.API.Enums.DamageType.Warhead)
        {
            ev.IsAllowed = false;
        }
    }

    private static bool IsNtfRole(RoleTypeId role)
        => role is RoleTypeId.NtfPrivate or RoleTypeId.NtfSpecialist or RoleTypeId.NtfSergeant
            or RoleTypeId.NtfCaptain or RoleTypeId.FacilityGuard;

    // ------------------------------------------------------------------
    //  Взаимодействие с панелями
    // ------------------------------------------------------------------

    private void OnKeybindPressed(Player player, CustomKeybind keybind)
    {
        if (keybind != CustomKeybind.E)
            return;

        StationsManager.TryInvokeNearest(player);
    }

    private void Interact(Player player, bool isPanel1)
    {
        if (!_config.IsEnabled || player == null || !player.IsAlive)
            return;

        // Уже активировано — не-отряд может сорвать CO2
        if (_activated)
        {
            TryCancel(player, isPanel1);
            return;
        }

        if (ConceptsController.IsActivated)
            return;

        if (!IsSquadMember(player))
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#94a3b8>Панель заблокирована. Требуется доступ отряда СО₂.</color>", 2f, "co2", 20);
            return;
        }

        if (_activators.Contains(player.UserId))
            return;

        Timing.RunCoroutine(InteractCoroutine(player, isPanel1), $"{ProcessTag}_p{(isPanel1 ? 1 : 2)}_{player.Id}");
    }

    private IEnumerator<float> InteractCoroutine(Player player, bool isPanel1)
    {
        _activators.Add(player.UserId);
        SetIndicator(isPanel1, IndicatorActive);

        if (isPanel1) _door1Active = true;
        else _door2Active = true;

        BroadcastToAll($"<color=#14b1e0>{player.Nickname}</color> запустил активацию панели ({(isPanel1 ? 1 : 2)}/2)...");

        yield return Timing.WaitForSeconds(Mathf.Max(0.5f, _config.ActivateConfirmSeconds));

        bool otherActive = isPanel1 ? _door2Active : _door1Active;

        if (otherActive && !_activated)
        {
            Activate(new[] { player.UserId }.Concat(_activators).Distinct().ToList());
            yield break;
        }

        if (_activated)
            yield break;

        // Подтверждение не удалось — откат панели
        if (isPanel1) _door1Active = false;
        else _door2Active = false;

        SetIndicator(isPanel1, IndicatorIdle);
        _activators.Remove(player.UserId);
    }

    // ------------------------------------------------------------------
    //  Активация / Отмена / Процесс
    // ------------------------------------------------------------------

    private void Activate(List<string> activators)
    {
        _activated = true;
        ConceptsController.Activate("CO2");

        foreach (var userId in activators.Distinct())
        {
            NoRulesPlugin.Instance?.PlayerXp?.SetRawXp(userId, string.Empty,
                NoRulesPlugin.Instance.PlayerXp.GetXp(userId) + _config.MissionXp);
        }

        BroadcastToAll($"<color=#0099ff><b>⚠ CO2 АКТИВИРОВАН</b></color>\n<size=70%><color=#c2c2c2>Комплекс будет затоплен угарным газом. Покиньте facility или наденьте противогаз...</color></size>");

        TryPlayCassie(_config.CassieActivate);

        PlayClip("co2");

        SnapshotLights();
        ChangeAllRoomsColor(new Color32(20, 177, 224, 255));
        LockElevators();

        Timing.RunCoroutine(ProcessCoroutine(), ProcessTag);
    }

    private IEnumerator<float> ProcessCoroutine()
    {
        int roundId = _roundStartTime.GetHashCode();

        yield return Timing.WaitForSeconds(Mathf.Max(10f, _config.PoisonDelaySeconds));

        if (!_activated)
            yield break;

        _allowCancel = false;

        yield return Timing.WaitForSeconds(Mathf.Max(5f, _config.PoisonGraceSeconds));

        if (!_activated)
            yield break;

        LockElevators(force: true);

        Timing.RunCoroutine(PoisonLoop(), PoisonTag);

        yield return Timing.WaitForSeconds(Mathf.Max(10f, _config.EndAfterPoisonSeconds));

        if (!_activated)
            yield break;

        BroadcastToAll("<color=#0099ff><b>Комплекс загерметизирован. Выжившие отсутствуют.</b></color>");
        Timing.CallDelayed(3f, () =>
        {
            try { Round.Restart(); }
            catch (Exception ex) { Log.Error($"[CO2] Restart: {ex.Message}"); }
        });
    }

    private IEnumerator<float> PoisonLoop()
    {
        while (_activated)
        {
            foreach (var pl in Player.List)
            {
                try
                {
                    if (pl == null || !pl.IsAlive || pl.Role.Side == Side.Scp)
                        continue;

                    pl.Health = Mathf.Max(0f, pl.Health - _config.PoisonDamagePerSecond);
                }
                catch { }
            }

            yield return Timing.WaitForSeconds(1f);
        }
    }

    private void TryCancel(Player player, bool isPanel1)
    {
        if (!_allowCancel)
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#f87171>Процесс необратим.</color>", 2f, "co2", 20);
            return;
        }

        if (IsSquadMember(player))
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#facc15>Вы — член отряда. CO2 нельзя отменять своим же.</color>", 2.5f, "co2", 20);
            return;
        }

        Deactivate(isPanel1, player);
    }

    private void Deactivate(bool isPanel1, Player canceller)
    {
        LightsOutBrief();
        SetIndicator(isPanel1, IndicatorIdle);

        if (isPanel1) _door1Active = false;
        else _door2Active = false;

        bool otherStillActive = isPanel1 ? _door2Active : _door1Active;
        if (otherStillActive)
        {
            Timing.CallDelayed(Mathf.Max(1f, _config.ActivateConfirmSeconds), () =>
            {
                if (!_activated)
                    return;

                if (isPanel1) _door1Active = true;
                else _door2Active = true;

                SetIndicator(isPanel1, IndicatorActive);
            });
            return;
        }

        // Полная деактивация
        RestoreLights();
        UnlockElevators();

        TryPlayCassie(_config.CassieCancel);
        PlayClip("co2_cancel");

        Timing.KillCoroutines(ProcessTag);
        Timing.KillCoroutines(PoisonTag);

        AlphaStopSafe();
        ConceptsController.Disable();
        _activated = false;
        _activators.Clear();

        BroadcastToAll("<color=#a3e635><b>Активация CO2 отменена. Комплекс возвращён под контроль.</b></color>");
    }

    private void AlphaStopSafe()
    {
        try { Warhead.Stop(); } catch { }
    }

    // ------------------------------------------------------------------
    //  Свет / двери / звук / broadcast
    // ------------------------------------------------------------------

    private void SnapshotLights()
    {
        _originalRoomColors.Clear();
        foreach (var room in Room.List)
        {
            try { _originalRoomColors[room] = room.Color; } catch { }
        }
    }

    private void ChangeAllRoomsColor(Color32 color)
    {
        foreach (var room in Room.List)
        {
            try { room.Color = color; } catch { }
        }
    }

    private void RestoreLights()
    {
        foreach (var kvp in _originalRoomColors)
        {
            try { kvp.Key.Color = kvp.Value; } catch { }
        }

        _originalRoomColors.Clear();
    }

    private void LightsOutBrief()
    {
        foreach (var room in Room.List)
        {
            try { room.Color = new Color32(10, 10, 12, 255); } catch { }
        }

        Timing.CallDelayed(1.5f, RestoreLights);
    }

    private void LockElevators(bool force = false)
    {
        try
        {
            foreach (var door in Door.List.Where(d =>
                         d.Type is DoorType.ElevatorGateA or DoorType.ElevatorGateB))
            {
                door.IsOpen = false;

                if (force || !_lockedDoors.Contains(door))
                {
                    door.ChangeLock(DoorLockType.Regular079);
                    if (!_lockedDoors.Contains(door))
                        _lockedDoors.Add(door);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[CO2] LockElevators: {ex.Message}");
        }
    }

    private void UnlockElevators()
    {
        foreach (var door in _lockedDoors)
        {
            try { door.ChangeLock(DoorLockType.None); } catch { }
        }

        _lockedDoors.Clear();
    }

    /// <summary>
    /// CASSIE-объявление (совместимо с разными версиями EXILED).
    /// </summary>
    private static void TryPlayCassie(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            // Exiled.API.Features.Cassie.Message(text, isHeld, isSubtle, isNoisy)
            Exiled.API.Features.Cassie.Message(text, false, false, false);
        }
        catch
        {
            try
            {
                // Старая сигнатура (text, isHeld, isSubtle)
                Exiled.API.Features.Cassie.Message(text, false, false);
            }
            catch (Exception ex)
            {
                Log.Debug($"[CO2] CASSIE не сработала: {ex.Message}");
            }
        }
    }

    private void PlayClip(string clipName)
    {
        try
        {
            if (!AudioClipStorage.AudioClips.ContainsKey(clipName))
                return;

            var ap = AudioPlayer.CreateOrGet(AudioKey, onIntialCreation: p =>
            {
                p.AddSpeaker("Main", isSpatial: false, maxDistance: 5000f, volume: 1f);
            });

            ap.AddClip(clipName, destroyOnEnd: true);
        }
        catch { }
    }

    private void BroadcastToAll(string message)
    {
        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, message, 6f, "co2", 22);
    }
}
