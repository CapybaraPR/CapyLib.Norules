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
using Exiled.API.Features.Toys;
using MEC;
using Mirror;
using UnityEngine;
using Light = Exiled.API.Features.Toys.Light;

namespace Capy.NoRules.Features.Concepts.Scp008;

/// <summary>
/// Концепт «SCP-008» (зомби-вирус):
/// В четырёх комнатах комплекса (173, 049, 939, EZ-убежище) построены вирусные
/// трубки. SCP может открыть трубу ([E] рядом) — через OpenDurationToOutbreak секунд
/// начинается вспышка вируса: все люди получают кровотечение и отравление на весь
/// раунд + периодический урон; SCP-сторона лечится. Каждая открытая труба усиливает вспышку.
/// </summary>
public sealed class Scp008Concept
{
    private readonly Scp008Config _config;

    public sealed class Tube
    {
        public string RoomName { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public bool Opened { get; set; }
        public Primitive? Glass { get; set; }
        public Light? Glow { get; set; }
    }

    private readonly List<Tube> _tubes = new();
    private readonly List<GameObject> _spawnedObjects = new();
    private CoroutineHandle _outbreakLoop;
    private bool _enabled;

    public bool OutbreakActive { get; private set; }

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
        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
        AssKeybinds.OnKeybindPressed -= OnKeybindPressed;

        Teardown();
        OutbreakActive = false;
    }

    private void OnRestartingRound()
    {
        Timing.KillCoroutines("scp008_outbreak");
        Teardown();
        OutbreakActive = false;
    }

    private void Track(GameObject go) => _spawnedObjects.Add(go);

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
        Teardown();

        if (!_config.IsEnabled) return;

        var tubeRooms = new Dictionary<RoomType, Color32>
        {
            [RoomType.Lcz173] = new(124, 58, 237, 255),
            [RoomType.Hcz049] = new(74, 222, 128, 255),
            [RoomType.Hcz939] = new(239, 68, 68, 255),
            [RoomType.EzShelter] = new(56, 189, 248, 255)
        };

        foreach (var tubeRoomKvp in tubeRooms)
        {
            var roomType = tubeRoomKvp.Key;
            var glowColor = tubeRoomKvp.Value;
            try
            {
                var room = Room.List.FirstOrDefault(r => r.Type == roomType);
                if (room == null) continue;

                Vector3 pos = room.Position + room.Rotation *
                    new Vector3(_config.OffsetX, 0.55f + _config.OffsetY, _config.OffsetZ);

                BuildTube(pos, glowColor, roomType.ToString());
            }
            catch (Exception ex)
            {
                Log.Error($"[SCP-008] Труба в {roomType}: {ex.Message}");
            }
        }
    }

    private void BuildTube(Vector3 pos, Color32 glowColor, string roomName)
    {
        // Спавним детальную JSON-схематику труб
        var schematic = SchematicLoader.Spawn("VirusTube", pos, Quaternion.identity);
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

        // Динамическое HDR-свечение (меняется при открытии)
        var glow = Light.Create(
            position: pos + Vector3.up * 0.75f,
            rotation: null,
            scale: Vector3.one,
            spawn: false,
            color: glowColor);

        if (glow != null)
        {
            glow.Intensity = 4f;
            glow.Range = 2.2f;
            glow.Spawn();
            Track(glow.GameObject);
        }

        _tubes.Add(new Tube
        {
            RoomName = roomName,
            Position = pos,
            Glow = glow
        });
    }

    // ------------------------------------------------------------------
    //  Взаимодействие
    // ------------------------------------------------------------------

    private void OnKeybindPressed(Player player, CustomKeybind keybind)
    {
        if (keybind != CustomKeybind.E || !_config.IsEnabled)
            return;

        if (!player.IsAlive || !player.IsScp)
            return;

        foreach (var tube in _tubes.Where(t => !t.Opened))
        {
            float dist = (player.Position - tube.Position).sqrMagnitude;
            float radius = Mathf.Max(1.5f, _config.TubeRadius);

            if (dist <= radius * radius)
            {
                OpenTube(player, tube);
                return;
            }
        }
    }

    private void OpenTube(Player opener, Tube tube)
    {
        tube.Opened = true;

        // Колба «разбивается»: стекло исчезает, свечение становится ядовито-зелёным и ярким
        try
        {
            if (tube.Glass != null && tube.Glass.GameObject != null)
                tube.Glass.GameObject.SetActive(false);

            if (tube.Glow != null)
            {
                tube.Glow.Color = new Color32(132, 204, 22, 255);
                tube.Glow.Intensity = 8f;
                tube.Glow.Range = 6f;
            }
        }
        catch { }

        int openedCount = _tubes.Count(t => t.Opened);
        bool first = openedCount == 1;

        if (first)
        {
            OutbreakActive = true;
            ConceptsController.Activate("SCP-008");

            Timing.RunCoroutine(OutbreakLoop(), "scp008_outbreak");

            BroadcastToAll(
                "<color=#84cc16><b>☣ ОБНАРУЖЕНА УТЕЧКА SCP-008</b></color>\n" +
                "<size=70%><color=#c2c2c2>Вирус распространяется по вентиляции комплекса...\n" +
                "Персоналу немедленно покинуть комплекс!</color></size>");
        }
        else
        {
            BroadcastToAll($"<color=#84cc16>☣ Открыта ещё одна трубка SCP-008 ({openedCount}/{_tubes.Count})! Вирус усиливается...</color>");
        }

        try
        {
            Exiled.API.Features.Cassie.Message(_config.CassieOutbreak, false, false, false);
        }
        catch { }

        opener.ShowZoneHint(HintZone.Notification,
            $"<color=#84cc16>☣ Труба в зоне {tube.RoomName} открыта!</color>", 3f, "scp008", 22);

        // Награда открывшему
        NoRulesPlugin.Instance?.PlayerXp?.SetRawXp(opener.UserId, string.Empty,
            NoRulesPlugin.Instance.PlayerXp.GetXp(opener.UserId) + 50f);
    }

    private IEnumerator<float> OutbreakLoop()
    {
        float elapsed = 0f;

        while (OutbreakActive)
        {
            yield return Timing.WaitForSeconds(Mathf.Max(2f, _config.TickSeconds));
            elapsed += Mathf.Max(2f, _config.TickSeconds);

            int openedTubes = _tubes.Count(t => t.Opened);
            if (openedTubes == 0) yield break;

            float damagePerTick = _config.OutbreakDamagePerTick * openedTubes;

            foreach (var p in Player.List)
            {
                try
                {
                    if (p == null || !p.IsAlive || !p.IsHuman)
                        continue;

                    // Люди страдают от вируса
                    p.EnableEffect(EffectType.Bleeding, 1, 10f);
                    p.Health = Mathf.Max(1f, p.Health - damagePerTick);
                }
                catch { }
            }

            // SCP-сторона лечится от вспышки (они же её устроили)
            if (_config.HealScpsOnTick)
            {
                foreach (var scp in Player.List.Where(x => x is { IsAlive: true, Role.Side: Side.Scp }))
                {
                    try { scp.Heal(_config.ScpHealAmount); } catch { }
                }
            }

            // Раз в минуту — психоделическое оповещение выжившим
            if ((int)elapsed % 60 < _config.TickSeconds)
            {
                BroadcastToAll("<color=#84cc16>☣ Концентрация SCP-008 в воздухе повышается...</color>");
            }
        }
    }

    private void BroadcastToAll(string message)
    {
        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, message, 6f, "scp008", 22);
    }
}
