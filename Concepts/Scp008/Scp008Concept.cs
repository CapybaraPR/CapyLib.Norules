using System;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Audio;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.ServerSpecific;
using Capy.Engine.Studio.Core;
using Capy.NoRules.Config;
using Capy.NoRules.Controllers;
using Capy.NoRules.Spawns;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Doors;
using Exiled.API.Features.Toys;
using MEC;
using Mirror;
using PlayerRoles;
using UnityEngine;
using Light = Exiled.API.Features.Toys.Light;

namespace Capy.NoRules.Concepts.Scp008;

/// <summary>
/// Концепт «SCP-008» (Длань Змея и зомби-вирус):
/// 1. Отряд «Длань Змея» спавнится через 5-8 минут раунда из спектаторов (союз с SCP).
/// 2. В 4 комнатах (173, 049, 939, EZ-убежище) установлены вирусные вентили.
/// 3. Длань Змея открывает все 4 вентиля и запускает главную консоль в HCZ Test.
/// 4. Людям достаточно перекрыть 2 вентиля из 4 (или 1 при деконтаминации LCZ), чтобы сорвать запуск.
/// 5. Таймлайн синхронизирован под саундтрек 2:35 (155 сек): лифты на улицу блокируются строго в финале,
///    а погибшие от вируса люди восстают в виде зомби SCP-049-2.
/// </summary>
public sealed class Scp008Concept
{
    private const string SerpentsHandTag = "SerpentsHandPlayer";
    private const string ProcessTag = "scp008_process";
    private const string PoisonTag = "scp008_poison";
    private const string SpawnTag = "scp008_spawn";

    private readonly Scp008Config _config;

    public sealed class Tube
    {
        public RoomType RoomType { get; set; }
        public Vector3 Position { get; set; }
        public bool Opened { get; set; }
        public Light? Glow { get; set; }
    }

    private readonly List<Tube> _tubes = new();
    private readonly List<GameObject> _spawnedObjects = new();
    private readonly Dictionary<Room, Color> _savedRoomColors = new();
    private readonly HashSet<string> _serpentsHandPlayers = new();

    private Vector3 _controlPanelPos;
    private bool _controlPanelSpawned;
    private bool _enabled;
    private bool _activated;
    private bool _allowCancel = true;
    private bool _spawnedThisRound;

    public bool IsActivated => _activated;

    public Scp008Concept(Scp008Config config)
    {
        _config = config;
    }

    // ------------------------------------------------------------------
    //  Lifecycle
    // ------------------------------------------------------------------

    public void Enable()
    {
        if (!_config.IsEnabled) return;
        _enabled = true;

        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
        Exiled.Events.Handlers.Player.Hurting += OnHurting;
        Exiled.Events.Handlers.Player.Died += OnDied;
        Exiled.Events.Handlers.Player.Left += OnLeft;
        Exiled.Events.Handlers.Player.ChangingRole += OnChangingRole;
        Exiled.Events.Handlers.Player.UnlockingGenerator += OnUnlockingGenerator;
        Exiled.Events.Handlers.Player.ActivatingGenerator += OnActivatingGenerator;
        Exiled.Events.Handlers.Scp173.AddingObserver += OnAddingObserver;
        Exiled.Events.Handlers.Scp096.AddingTarget += OnAddingTarget;
        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
        Exiled.Events.Handlers.Player.Hurting -= OnHurting;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        Exiled.Events.Handlers.Player.Left -= OnLeft;
        Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;
        Exiled.Events.Handlers.Player.UnlockingGenerator -= OnUnlockingGenerator;
        Exiled.Events.Handlers.Player.ActivatingGenerator -= OnActivatingGenerator;
        Exiled.Events.Handlers.Scp173.AddingObserver -= OnAddingObserver;
        Exiled.Events.Handlers.Scp096.AddingTarget -= OnAddingTarget;
        AssKeybinds.OnKeybindPressed -= OnKeybindPressed;

        ResetState();
    }

    private void OnRestartingRound()
    {
        ResetState();
    }

    private void ResetState()
    {
        Timing.KillCoroutines(ProcessTag);
        Timing.KillCoroutines(PoisonTag);
        Timing.KillCoroutines(SpawnTag);

        try { AudioToggle.DestroyGlobal("scp008"); } catch { }
        try { AudioToggle.DestroyGlobal("scp008soundtrack"); } catch { }
        try { AudioToggle.DestroyGlobal("co2cancel"); } catch { }

        RestoreLights();
        UnlockElevators();

        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ClearCapyHint("scp008");

        Teardown();

        _activated = false;
        _allowCancel = true;
        _spawnedThisRound = false;
        _controlPanelSpawned = false;
        _serpentsHandPlayers.Clear();
    }

    private void Track(GameObject go)
    {
        if (go != null) _spawnedObjects.Add(go);
    }

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
        _tubes.Clear();
    }

    private void OnRoundStarted()
    {
        ResetState();

        if (!_config.IsEnabled) return;

        // Спавним 4 вентиля в комнатах комплекса
        SpawnTubes();

        // Спавним Консоль Управления в HCZ Test
        SpawnControlPanel();

        // Запускаем корутину попытки спавна отряда Длани Змея
        Timing.RunCoroutine(SpawnSerpentsHandCoroutine(), SpawnTag);
    }

    // ------------------------------------------------------------------
    //  Спавн Вентилей и Консоли
    // ------------------------------------------------------------------

    private void SpawnTubes()
    {
        var tubeRooms = new[]
        {
            RoomType.Lcz173,
            RoomType.Hcz049,
            RoomType.Hcz939,
            RoomType.EzShelter
        };

        foreach (var roomType in tubeRooms)
        {
            try
            {
                var room = Room.List.FirstOrDefault(r => r.Type == roomType);
                if (room == null) continue;

                Vector3 localOffset = roomType switch
                {
                    RoomType.Lcz173 => new Vector3(0f, 0.5f, -3f),
                    RoomType.Hcz049 => new Vector3(0f, 0.5f, 3f),
                    RoomType.Hcz939 => new Vector3(-2f, 0.5f, 0f),
                    RoomType.EzShelter => new Vector3(0f, 0.5f, -2f),
                    _ => Vector3.zero
                };

                Vector3 pos = room.Position + room.Rotation * localOffset;
                BuildTube(pos, room.Rotation, roomType);
            }
            catch (Exception ex)
            {
                Log.Error($"[SCP-008] Ошибка спавна трубки в {roomType}: {ex.Message}");
            }
        }
    }

    private void BuildTube(Vector3 pos, Quaternion rot, RoomType roomType)
    {
        var schematic = SchematicLoader.Spawn("VirusTube", pos, rot);
        if (schematic != null)
        {
            foreach (var go in schematic.SpawnedGameObjects)
            {
                if (go != null) Track(go);
            }
            foreach (var prim in schematic.SpawnedPrimitives)
            {
                try { if (prim?.GameObject != null) Track(prim.GameObject); } catch { }
            }
        }

        // Индикатор состояния (Зеленый = закрыт, Красный = открыт)
        var glow = Light.Create(
            position: pos + Vector3.up * 0.8f,
            rotation: null,
            scale: Vector3.one,
            spawn: false,
            color: new Color32(34, 197, 94, 255));

        if (glow != null)
        {
            glow.Intensity = 3.5f;
            glow.Range = 3.0f;
            glow.Spawn();
            Track(glow.GameObject);
        }

        _tubes.Add(new Tube
        {
            RoomType = roomType,
            Position = pos,
            Opened = false,
            Glow = glow
        });
    }

    private void SpawnControlPanel()
    {
        try
        {
            var room = Room.List.FirstOrDefault(r => r.Type == RoomType.HczServerRoom)
                       ?? Room.List.FirstOrDefault(r => r.Type == RoomType.Hcz049);
            if (room == null) return;

            _controlPanelPos = room.Position + room.Rotation * new Vector3(0f, 0.5f, 2f);
            var schematic = SchematicLoader.Spawn("CO2Panel", _controlPanelPos, room.Rotation);
            if (schematic != null)
            {
                foreach (var go in schematic.SpawnedGameObjects)
                {
                    if (go != null) Track(go);
                }
            }

            _controlPanelSpawned = true;
        }
        catch (Exception ex)
        {
            Log.Error($"[SCP-008] Ошибка спавна консоли в HczServerRoom: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    //  Спавн Длани Змея
    // ------------------------------------------------------------------

    private IEnumerator<float> SpawnSerpentsHandCoroutine()
    {
        float delay = UnityEngine.Random.Range(_config.MinSpawnDelaySeconds, _config.MaxSpawnDelaySeconds);
        yield return Timing.WaitForSeconds(delay);

        while (!_spawnedThisRound && Round.IsStarted)
        {
            if (TrySpawnSerpentsHand())
                yield break;

            yield return Timing.WaitForSeconds(30f);
        }
    }

    private bool TrySpawnSerpentsHand()
    {
        if (_spawnedThisRound || _activated || Warhead.IsDetonated)
            return false;

        var spectators = Player.List.Where(p => p != null && p.IsConnected && p.Role.Type == RoleTypeId.Spectator).ToList();
        if (spectators.Count < _config.MinSpectators)
            return false;

        if (UnityEngine.Random.Range(0, 100) >= _config.SpawnChancePercent)
            return false;

        ShuffleList(spectators);
        int count = Math.Min(spectators.Count, _config.SquadSizeMax);

        for (int i = 0; i < count; i++)
        {
            SpawnOneSerpentsHand(spectators[i], i);
        }

        _spawnedThisRound = true;

        BroadcastToAll("<color=#16a34a><b>[ ДЛАНЬ ЗМЕЯ ПРОНИКЛА В КОМПЛЕКС ]</b></color>\n<size=70%><color=#cbd5e1>В Зону-02 проник диверсионный отряд. Цель: активация штамма SCP-008!</color></size>");
        return true;
    }

    private static void ShuffleList<T>(IList<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = UnityEngine.Random.Range(0, n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    private static readonly (Vector3 Offset, float Yaw)[] SurfaceSpawnPoints = new[]
    {
        (new Vector3(135.40f, -4.10f, -59.22f), 191.93f),
        (new Vector3(137.63f, -4.10f, -60.46f), 194.86f),
        (new Vector3(139.84f, -4.11f, -59.25f), 186.77f),
        (new Vector3(139.91f, -4.10f, -62.79f), 191.04f),
        (new Vector3(137.44f, -4.10f, -63.05f), 192.83f),
        (new Vector3(138.30f, -4.09f, -64.59f), 190.25f),
    };

    private void SpawnOneSerpentsHand(Player player, int index)
    {
        if (player == null || !player.IsConnected) return;

        SpawnManager.ApplySpawnProtection(player);
        player.Role.Set(RoleTypeId.Tutorial, SpawnReason.Respawn, RoleSpawnFlags.All);

        var surfaceRoom = Room.List.FirstOrDefault(r => r.Type == RoomType.Surface);
        if (surfaceRoom != null)
        {
            var pt = SurfaceSpawnPoints[index % SurfaceSpawnPoints.Length];
            Vector3 worldPos = surfaceRoom.Transform.TransformPoint(pt.Offset);
            player.Position = worldPos;
            player.Rotation = Quaternion.Euler(0f, pt.Yaw, 0f);
        }
        else
        {
            player.Position = new Vector3(0f, 302f, 5f);
        }

        _serpentsHandPlayers.Add(player.UserId);

        player.MaxHealth = 125f;
        player.Health = 125f;

        player.ClearInventory();
        player.AddItem(ItemType.KeycardChaosInsurgency);
        player.AddItem(ItemType.GunCrossvec);
        player.AddItem(ItemType.ArmorCombat);
        player.AddItem(ItemType.SCP207);
        player.AddItem(ItemType.SCP500);
        player.AddItem(ItemType.Medkit);
        player.AddItem(ItemType.Lantern);

        // Уникальный артефакт в зависимости от номера бойца
        if (index == 0)
            player.AddItem(ItemType.SCP268);
        else if (index == 1)
            player.AddItem(ItemType.SCP018);
        else
            player.AddItem(ItemType.SCP1853);

        player.CustomInfo = "<color=#16a34a>Длань Змея</color>";
        player.InfoArea = PlayerInfoArea.Nickname | PlayerInfoArea.Badge | PlayerInfoArea.CustomInfo | PlayerInfoArea.Role;

        player.ShowZoneHint(HintZone.TopCenter,
            "<color=#16a34a><b>ВЫ — БОЕЦ «ДЛАНИ ЗМЕЯ»</b></color>\n" +
            "<size=70%><color=#cbd5e1>Вы в союзе с SCP! Откройте 4 вентиля по комплексу и запустите консоль в HCZ Test!</color></size>",
            12f, "scp008_sh", 24);
    }

    private bool IsSerpentsHand(Player? player)
    {
        return player != null && _serpentsHandPlayers.Contains(player.UserId);
    }

    // ------------------------------------------------------------------
    //  Союз с SCP (Взаимная защита)
    // ------------------------------------------------------------------

    private void OnAddingObserver(Exiled.Events.EventArgs.Scp173.AddingObserverEventArgs ev)
    {
        // Длань Змея не останавливает SCP-173 взглядом
        if (ev.Observer != null && IsSerpentsHand(ev.Observer))
        {
            ev.IsAllowed = false;
        }
    }

    private void OnAddingTarget(Exiled.Events.EventArgs.Scp096.AddingTargetEventArgs ev)
    {
        // Длань Змея не провоцирует SCP-096 при взгляде
        if (ev.Target != null && IsSerpentsHand(ev.Target))
        {
            ev.IsAllowed = false;
        }
    }

    private void OnUnlockingGenerator(Exiled.Events.EventArgs.Player.UnlockingGeneratorEventArgs ev)
    {
        // Длань Змея не может разблокировать генераторы
        if (ev.Player != null && IsSerpentsHand(ev.Player))
        {
            ev.IsAllowed = false;
        }
    }

    private void OnActivatingGenerator(Exiled.Events.EventArgs.Player.ActivatingGeneratorEventArgs ev)
    {
        // Длань Змея не может активировать генераторы
        if (ev.Player != null && IsSerpentsHand(ev.Player))
        {
            ev.IsAllowed = false;
        }
    }

    private void OnHurting(Exiled.Events.EventArgs.Player.HurtingEventArgs ev)
    {
        if (ev.Attacker == null || ev.Player == null) return;

        bool attackerIsSH = IsSerpentsHand(ev.Attacker);
        bool targetIsSH = IsSerpentsHand(ev.Player);
        bool attackerIsSCP = ev.Attacker.Role.Side == Side.Scp;
        bool targetIsSCP = ev.Player.Role.Side == Side.Scp;

        // Длань Змея и SCP не наносят урон друг другу (включая карманное измерение 106)
        if ((attackerIsSH && targetIsSCP) || (attackerIsSCP && targetIsSH))
        {
            ev.IsAllowed = false;
            ev.Amount = 0f;
        }
        // Бойцы Длани Змея не могут дамажить своих соклановцев
        else if (attackerIsSH && targetIsSH && ev.Attacker != ev.Player)
        {
            ev.IsAllowed = false;
            ev.Amount = 0f;
        }
    }

    private void OnDied(Exiled.Events.EventArgs.Player.DiedEventArgs ev)
    {
        if (ev.Player != null)
            _serpentsHandPlayers.Remove(ev.Player.UserId);
    }

    private void OnLeft(Exiled.Events.EventArgs.Player.LeftEventArgs ev)
    {
        if (ev.Player != null)
            _serpentsHandPlayers.Remove(ev.Player.UserId);
    }

    private void OnChangingRole(Exiled.Events.EventArgs.Player.ChangingRoleEventArgs ev)
    {
        if (ev.Player != null && ev.NewRole != RoleTypeId.Tutorial)
            _serpentsHandPlayers.Remove(ev.Player.UserId);
    }

    // ------------------------------------------------------------------
    //  Взаимодействие с Вентилями и Консолью
    // ------------------------------------------------------------------

    private void OnKeybindPressed(Player player, CustomKeybind keybind)
    {
        if (keybind != CustomKeybind.E || !_config.IsEnabled || !player.IsAlive)
            return;

        float radius = Mathf.Max(1.5f, _config.InteractRadius);
        float radiusSqr = radius * radius;

        // 1. Проверка вентилей
        foreach (var tube in _tubes)
        {
            if ((player.Position - tube.Position).sqrMagnitude <= radiusSqr)
            {
                InteractWithTube(player, tube);
                return;
            }
        }

        // 2. Проверка Главной Консоли в HCZ Test
        if (_controlPanelSpawned && (player.Position - _controlPanelPos).sqrMagnitude <= radiusSqr)
        {
            InteractWithControlPanel(player);
        }
    }

    private void InteractWithTube(Player player, Tube tube)
    {
        // Если комната в LCZ и деконтаминация уже завершена
        if (tube.RoomType == RoomType.Lcz173 && Map.IsLczDecontaminated)
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#ef4444>Вентиль заблокирован протоколом очистки LCZ!</color>", 4f, "scp008", 20);
            return;
        }

        bool isSH = IsSerpentsHand(player);

        if (isSH)
        {
            // Длань Змея ОТКРЫВАЕТ вентиль
            if (tube.Opened)
            {
                player.ShowZoneHint(HintZone.Notification, "<color=#eab308>Этот вентиль уже открыт!</color>", 3f, "scp008", 20);
                return;
            }

            tube.Opened = true;
            if (tube.Glow != null)
                tube.Glow.Color = new Color32(239, 68, 68, 255); // Красный

            int activeCount = GetActiveTubesCount();
            player.ShowZoneHint(HintZone.Notification, $"<color=#16a34a><b>Вентиль открыт!</b></color> <color=#cbd5e1>({activeCount}/4 активно)</color>", 4f, "scp008", 22);

            UpdateHudStatus();
        }
        else
        {
            // Люди / Хаос ЗАКРЫВАЮТ вентиль
            if (!tube.Opened)
            {
                player.ShowZoneHint(HintZone.Notification, "<color=#22c55e>Этот вентиль перекрыт.</color>", 3f, "scp008", 20);
                return;
            }

            tube.Opened = false;
            if (tube.Glow != null)
                tube.Glow.Color = new Color32(34, 197, 94, 255); // Зеленый

            player.ShowZoneHint(HintZone.Notification, "<color=#22c55e><b>Вы перекрыли вентиль магистрали 008!</b></color>", 4f, "scp008", 22);

            // Если протокол уже запущен — проверяем условие отмены по давлению
            if (_activated)
            {
                CheckPressureCancel(player);
            }

            UpdateHudStatus();
        }
    }

    private void InteractWithControlPanel(Player player)
    {
        if (_activated)
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#eab308>Протокол выброса уже активен!</color>", 3f, "scp008", 20);
            return;
        }

        if (!IsSerpentsHand(player))
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#ef4444>Только Длань Змея может активировать протокол SCP-008!</color>", 4f, "scp008", 20);
            return;
        }

        int activeCount = GetActiveTubesCount();
        if (activeCount < 4)
        {
            player.ShowZoneHint(HintZone.Notification,
                $"<color=#ef4444><b>Недостаточно давления в магистрали!</b></color>\n" +
                $"<size=70%><color=#cbd5e1>Открыто вентилей: {activeCount}/4. Откройте все вентили!</color></size>",
                5f, "scp008", 22);
            return;
        }

        // Запуск протокола SCP-008!
        Activate(player);
    }

    private int GetActiveTubesCount()
    {
        int count = 0;
        foreach (var tube in _tubes)
        {
            if (tube.RoomType == RoomType.Lcz173 && Map.IsLczDecontaminated)
                count++;
            else if (tube.Opened)
                count++;
        }
        return count;
    }

    private void CheckPressureCancel(Player player)
    {
        if (!_activated || !_allowCancel)
            return;

        int closedTubes = _tubes.Count(t => !t.Opened);
        bool lczDecon = Map.IsLczDecontaminated;

        // Условие отмены: перекрыто 2 вентиля (или 1, если LCZ деконтаминирована)
        int requiredClosed = lczDecon ? 1 : 2;

        if (closedTubes >= requiredClosed)
        {
            // Отмена протокола по падению давления!
            CancelOutbreak();
            BroadcastToAll("<color=#22c55e><b>[ ДАВЛЕНИЕ 008 СБРОШЕНО ]</b></color>\n<size=70%><color=#cbd5e1>Вентили магистрали перекрыты. Протокол выброса SCP-008 сорван!</color></size>");
        }
        else
        {
            BroadcastToAll($"<color=#eab308><b>[ ВНИМАНИЕ ]</b> Перекрыт вентиль 008! Закройте ещё {requiredClosed - closedTubes} для срыва!</color>");
        }
    }

    // ------------------------------------------------------------------
    //  Активация и Процесс Выброса (2:35 / 155 сек)
    // ------------------------------------------------------------------

    private void Activate(Player initiator)
    {
        _activated = true;
        _allowCancel = true;

        BroadcastToAll("<color=#16a34a><b>☣ ПРОТОКОЛ SCP-008 АКТИВИРОВАН ☣</b></color>\n<size=70%><color=#cbd5e1>Длань Змея запустила подачу штамма! Перекройте 2 вентиля чтобы сбить давление!</color></size>");

        TryPlayCassie(_config.CassieActivate);

        try { AudioToggle.CreateGlobal("scp008", volume: 1.0f); } catch { }

        SnapshotLights();
        ChangeAllRoomsColor(new Color32(35, 195, 55, 255)); // Атмосферный ядовито-зелёный свет

        NoRulesPlugin.Instance?.PlayerXp?.SetRawXp(initiator.UserId, string.Empty,
            NoRulesPlugin.Instance.PlayerXp.GetXp(initiator.UserId) + _config.MissionXp);

        Timing.RunCoroutine(ProcessCoroutine(), ProcessTag);
    }

    private IEnumerator<float> ProcessCoroutine()
    {
        // 1. Окно отмены (110 секунд от старта): лифты работают, игроки перекрывают вентили
        yield return Timing.WaitForSeconds(Mathf.Max(10f, _config.CancelWindowSeconds));

        if (!_activated)
            yield break;

        _allowCancel = false;
        BroadcastToAll("<color=#ef4444><b>[ ТОЧКА НЕВОЗВРАТА SCP-008 ]</b></color>\n<size=70%><color=#cbd5e1>Отмена заблокирована! Комплекс переходит в режим изоляции.</color></size>");

        // 2. Пауза до кульминации саундтрека (45 секунд, 110 + 45 = 155 секунд / 2:35)
        yield return Timing.WaitForSeconds(Mathf.Max(5f, _config.GraceSeconds));

        if (!_activated)
            yield break;

        // 3. ФИНАЛ (2:35): Лифты на Поверхность запечатываются (как при боеголовке) и начинается пуск вируса
        LockElevators(force: true);
        BroadcastToAll("<color=#16a34a><b>[ ВЫБРОС ШТАММА SCP-008 ]</b></color>\n<size=70%><color=#cbd5e1>Внутренний комплекс загерметизирован. Все погибшие восстанут в виде нежити!</color></size>");

        Timing.RunCoroutine(PoisonLoop(), PoisonTag);
    }

    private IEnumerator<float> PoisonLoop()
    {
        while (_activated)
        {
            foreach (var pl in Player.List)
            {
                try
                {
                    // Вирус не действует на SCP и на игроков на Поверхности
                    if (pl == null || !pl.IsAlive || pl.Role.Side == Side.Scp || pl.Zone == ZoneType.Surface)
                        continue;

                    float dmg = _config.PoisonDamagePerSecond;
                    if (pl.Health > dmg)
                    {
                        pl.Health -= dmg;
                    }
                    else
                    {
                        // При смерти от вируса игрок мгновенно обращается в зомби SCP-049-2!
                        pl.Role.Set(RoleTypeId.Scp0492, SpawnReason.Respawn, RoleSpawnFlags.None);
                        pl.ShowZoneHint(HintZone.Notification, "<color=#16a34a><b>Вы погибли от вируса и восстали в виде Зомби (SCP-049-2)!</b></color>", 7f, "scp008_zombie", 24);
                    }
                }
                catch { }
            }

            yield return Timing.WaitForSeconds(1f);
        }
    }

    private void CancelOutbreak()
    {
        _activated = false;
        Timing.KillCoroutines(ProcessTag);
        Timing.KillCoroutines(PoisonTag);

        try { AudioToggle.DestroyGlobal("scp008"); } catch { }
        try { AudioToggle.CreateGlobal("co2cancel", volume: 1.0f); } catch { }

        TryPlayCassie(_config.CassieCancel);

        RestoreLights();
        UnlockElevators();
    }

    // ------------------------------------------------------------------
    //  HUD и Индикация
    // ------------------------------------------------------------------

    private void UpdateHudStatus()
    {
        int active = GetActiveTubesCount();
        string text = $"<color=#16a34a><b>[ ВЕНТИЛИ SCP-008: {active}/4 АКТИВНО ]</b></color>";

        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
        {
            if (IsSerpentsHand(p))
                p.ShowZoneHint(HintZone.TopCenter, text, 4f, "scp008_hud", 20);
        }
    }

    // ------------------------------------------------------------------
    //  Освещение и Лифты
    // ------------------------------------------------------------------

    private void SnapshotLights()
    {
        _savedRoomColors.Clear();
        foreach (var r in Room.List)
        {
            if (r != null) _savedRoomColors[r] = r.Color;
        }
    }

    private void ChangeAllRoomsColor(Color color)
    {
        foreach (var r in Room.List)
        {
            if (r != null) r.Color = color;
        }
    }

    private void RestoreLights()
    {
        foreach (var kv in _savedRoomColors)
        {
            try { if (kv.Key != null) kv.Key.Color = kv.Value; } catch { }
        }
        _savedRoomColors.Clear();
    }

    private void LockElevators(bool force = false)
    {
        foreach (var d in Door.List)
        {
            if (d is ElevatorDoor ed)
            {
                if (force || ed.ElevatorType == ElevatorType.GateA || ed.ElevatorType == ElevatorType.GateB)
                {
                    ed.IsOpen = false;
                    ed.ChangeLock(DoorLockType.Regular079);
                }
            }
        }
    }

    private void UnlockElevators()
    {
        foreach (var d in Door.List)
        {
            if (d is ElevatorDoor ed)
            {
                ed.Unlock();
            }
        }
    }

    private void TryPlayCassie(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        try { Exiled.API.Features.Cassie.Message(message, false, false, false); } catch { }
    }

    private void BroadcastToAll(string message)
    {
        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, message, 6f, "scp008", 22);
    }
}
