using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using PlayerRoles;
using Exiled.API.Features;
using CommandSystem;
using Exiled.Events.EventArgs.Player;
using Exiled.API.Features.Toys;
using Exiled.API.Features.Pickups;
using Exiled.API.Features.Doors;
using MEC;
using Mirror;
using UnityEngine;
using Light = Exiled.API.Features.Toys.Light;

namespace Capy.NoRules.Features.Concepts.Ragnarok;

/// <summary>
/// Концепт «Рагнарёк» (культ ☦):
/// Игроки из списка священников (конфиг) могут стать Священником (.priest) —
/// получают нимб и свечение над головой. Любой живой может Уверовать (.believe).
/// Священник командой .pray в crossing-комнате при 3+ верующих рядом запускает
/// ритуал: свечи, запирание комнаты, godmode верующим, опускание Ковчега,
/// физическое вскрытие и выпадение наград.
/// </summary>
public sealed class RagnarokConcept
{
    private readonly RagnarokConfig _config;

    private readonly ConcurrentDictionary<string, DateTime> _priests = new(); // UserId -> время становления
    private readonly HashSet<string> _believers = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (CoroutineHandle Handle, List<GameObject> Objects)> _nimbs = new();
    private readonly List<GameObject> _ritualObjects = new();
    private bool _praying;

    public RagnarokConcept(RagnarokConfig config)
    {
        _config = config;
    }

    public void Enable()
    {
        if (!_config.IsEnabled) return;

        Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
        Exiled.Events.Handlers.Player.Died += OnDied;
        Exiled.Events.Handlers.Player.ChangingRole += OnChangingRole;
        Exiled.Events.Handlers.Player.Left += OnLeft;
    }

    public void Disable()
    {
        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;
        Exiled.Events.Handlers.Player.Left -= OnLeft;

        ClearAll();
    }

    private void OnWaitingForPlayers() => ClearAll();

    private void OnRestartingRound()
    {
        Timing.KillCoroutines("ragnarok_ritual");
        ClearAll();
    }

    private void ClearAll()
    {
        _priests.Clear();
        _believers.Clear();
        _praying = false;

        foreach (var kvp in _nimbs)
        {
            var handle = kvp.Value.Handle;
            var objects = kvp.Value.Objects;

            Timing.KillCoroutines(handle);

            foreach (var go in objects)
            {
                try
                {
                    if (go != null)
                        UnityEngine.Object.Destroy(go);
                }
                catch { }
            }
        }

        _nimbs.Clear();

        foreach (var go in _ritualObjects)
        {
            try
            {
                if (go != null)
                    UnityEngine.Object.Destroy(go);
            }
            catch { }
        }

        _ritualObjects.Clear();
    }

    // ------------------------------------------------------------------
    //  Роли
    // ------------------------------------------------------------------

    public string TryBecomePriest(Player player)
    {
        if (!_config.IsEnabled || player == null || !player.IsVerified)
            return "Недоступно.";

        bool allowed = _config.PriestSteamIds.Any(id =>
            player.UserId.IndexOf(id.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

        if (!allowed)
            return "Священник недоступен для вашего аккаунта.";

        if (_priests.ContainsKey(player.UserId))
            return "Вы уже являетесь Священником.";

        if (!player.IsAlive)
            return "Вы мертвы...";

        if (player.Role.Side == Side.Scp)
            return "За SCP нельзя стать Священником.";

        _priests[player.UserId] = DateTime.UtcNow;
        StartNimbus(player);

        player.ShowZoneHint(HintZone.TopCenter,
            "<color=#f2d410>✝ Вы стали <b>СВЯЩЕННИКОМ</b></color>\n\n" +
            "<color=#ffd285><b>РИТУАЛ ПРИЗЫВА:</b></color>\n" +
            "<size=65%><color=#c2c2c2>1. Другие игроки пишут .rag believe чтобы стать Верующими\n" +
            "2. Соберите 2+ Верующих в crossing-комнате (перекрёсток)\n" +
            "3. Пропишите .rag pray для начала ритуала\n\n" +
            "Ритуал длится 90 секунд. Комната будет заперта.\n" +
            "После завершения из Ковчега выпадет ОРУЖИЕ!</color></size>\n\n" +
            "<color=#6f6f6f>Верующие: .rag believe | Призыв: .rag pray</color>",
            15f, "rag_priest", 18);

        return "Вы стали Священником. Команды: .believe (для других), .pray (ритуал).";
    }

    public string TryBelieve(Player player)
    {
        if (!_config.IsEnabled || player == null || !player.IsVerified)
            return "Недоступно.";

        if (_priests.ContainsKey(player.UserId))
            return "Священник не может уверовать.";

        if (_believers.Contains(player.UserId))
            return "Вы уже уверовали.";

        if (!player.IsAlive || player.Role.Side == Side.Scp)
            return "За SCP нельзя уверовать.";

        _believers.Add(player.UserId);

        player.ShowZoneHint(HintZone.Notification,
            "<color=#93c5fd>✝ Вы уверовали. Найдите Священника и участвуйте в призыве!</color>",
            4f, "rag_believe", 20);

        return string.Empty;
    }

    public void TryPray(Player player)
    {
        if (!_config.IsEnabled || player == null)
            return;

        if (!_priests.ContainsKey(player.UserId))
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#f87171>Вы не Священник.</color>", 2f, "rag", 20);
            return;
        }

        if (_praying)
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#facc15>Призыв уже начат или завершён.</color>", 2.5f, "rag", 20);
            return;
        }

        if (Round.ElapsedTime.TotalMinutes < 3)
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#f87171>Начать призыв можно только после 3 минут раунда.</color>", 2.5f, "rag", 20);
            return;
        }

        var room = player.CurrentRoom;
        if (room == null || !IsCrossingRoom(room.Type))
        {
            player.ShowZoneHint(HintZone.Notification,
                "<color=#f87171>Эта комната не подходит для призыва. Нужна crossing-комната.</color>", 3f, "rag", 20);
            return;
        }

        Vector3 roomPos;
        try { roomPos = room.Position; }
        catch { roomPos = player.Position; }

        var participants = Player.List.Where(x =>
            x != null && x.IsAlive &&
            (_priests.ContainsKey(x.UserId) || _believers.Contains(x.UserId)) &&
            (x.Position - roomPos).sqrMagnitude <= 11f * 11f).ToList();

        if (participants.Count < Math.Max(2, _config.MinParticipants))
        {
            player.ShowZoneHint(HintZone.Notification,
                $"<color=#f87171>Число Верующих и Священников рядом менее {_config.MinParticipants}.</color>", 3f, "rag", 20);
            return;
        }

        Timing.RunCoroutine(RitualCoroutine(player, room, roomPos, participants), "ragnarok_ritual");
    }

    private static bool IsCrossingRoom(RoomType type)
        => type is RoomType.LczCrossing or RoomType.HczCrossing
            or RoomType.HczEzCheckpointA or RoomType.HczEzCheckpointB
            or RoomType.EzCrossing;

    // ------------------------------------------------------------------
    //  Нимб над головой священника
    // ------------------------------------------------------------------

    private void StartNimbus(Player priest)
    {
        var objects = new List<GameObject>();
        var handle = Timing.RunCoroutine(NimbusCoroutine(priest, objects));

        _nimbs[priest.UserId] = (handle, objects);
    }

    private IEnumerator<float> NimbusCoroutine(Player priest, List<GameObject> objects)
    {
        // Нимб: золотое кольцо (цилиндр тонкий) + HDR-свет
        var ring = Primitive.Create(
            primitiveType: PrimitiveType.Cylinder,
            flags: AdminToys.PrimitiveFlags.Visible,
            position: priest.Position + Vector3.up * 2.1f,
            rotation: new Vector3(0f, 0f, 90f),
            scale: new Vector3(0.02f, 0.28f, 0.28f),
            spawn: true,
            color: new Color32(255, 215, 0, 255));

        var glow = Light.Create(
            position: priest.Position + Vector3.up * 2.1f,
            rotation: null,
            scale: Vector3.one,
            spawn: false,
            color: new Color32(255, 215, 0, 255));

        if (glow != null)
        {
            glow.Intensity = 2f;
            glow.Range = 1.5f;
            glow.Spawn();
        }

        if (ring?.GameObject != null) objects.Add(ring.GameObject);
        if (glow?.GameObject != null) objects.Add(glow.GameObject);

        while (priest != null && priest.IsConnected && priest.IsAlive && _priests.ContainsKey(priest.UserId))
        {
            try
            {
                Vector3 headPos = priest.Position + Vector3.up * 2.05f;

                if (ring != null && ring.GameObject != null)
                    ring.GameObject.transform.position = headPos;

                if (glow != null && glow.GameObject != null)
                    glow.GameObject.transform.position = headPos + Vector3.up * 0.1f;
            }
            catch
            {
                yield break;
            }

            yield return Timing.WaitForOneFrame;
        }
    }

    // ------------------------------------------------------------------
    //  События
    // ------------------------------------------------------------------

    private void OnDied(DiedEventArgs ev)
    {
        if (ev.Player == null) return;

        RemovePerson(ev.Player);
    }

    private void OnChangingRole(ChangingRoleEventArgs ev)
    {
        if (ev.Player == null) return;

        if (ev.NewRole is RoleTypeId.None or RoleTypeId.Spectator or RoleTypeId.Overwatch)
            RemovePerson(ev.Player);
    }

    private void OnLeft(LeftEventArgs ev)
    {
        if (ev.Player == null) return;

        RemovePerson(ev.Player);
    }

    private void RemovePerson(Player player)
    {
        _priests.TryRemove(player.UserId, out _);
        _believers.Remove(player.UserId);

        if (_nimbs.TryRemove(player.UserId, out var nimbus))
        {
            Timing.KillCoroutines(nimbus.Handle);

            foreach (var go in nimbus.Objects)
            {
                try
                {
                    if (go != null)
                        UnityEngine.Object.Destroy(go);
                }
                catch { }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Ритуал призыва
    // ------------------------------------------------------------------

    private IEnumerator<float> RitualCoroutine(Player priest, Room room, Vector3 roomPos, List<Player> participants)
    {
        _praying = true;

        try
        {
            priest.ShowZoneHint(HintZone.TopCenter,
                "<color=#f2d410><b>✝ ПРИЗЫВ НАЧАТ</b></color>", 4f, "rag", 24);

            BroadcastNearby(roomPos, "<color=#f2d410>⛪ Свечи зажигаются... комната герметизируется...</color>");

            // 1. Свечи по углам (тело + фитиль + HDR-свет тёплого цвета)
            CreateCandles(roomPos);

            // 2. Верующие: обездвижены, горят, бессмертны на время ритуала
            foreach (var believer in participants)
            {
                try
                {
                    believer.EnableEffect(EffectType.SeveredHands, 100, 0f);
                    believer.EnableEffect(EffectType.Burned, 100, 0f);
                    believer.IsGodModeEnabled = true;
                }
                catch { }
            }

            // 3. Свет выключается, двери запираются
            try
            {
                foreach (var r in Room.List)
                    r.Color = new Color32(12, 10, 8, 255);
            }
            catch { }

            var lockedDoors = new List<Door>();

            try
            {
                foreach (var door in Door.List.Where(d => d.Room?.Type == room.Type))
                {
                    door.ChangeLock(DoorLockType.Regular079);
                    door.IsOpen = false;
                    lockedDoors.Add(door);
                }
            }
            catch { }

            yield return Timing.WaitForSeconds(Mathf.Max(10f, _config.RitualSeconds));

            // 4. Ковчег спускается с потолка
            BroadcastNearby(roomPos, "<color=#f2d410>⛪ Ковчег Бога ниспослан...</color>");

            var boxWalls = new List<Primitive>();
            Primitive? boxTop = null;
            Vector3 boxBase = roomPos + Vector3.up * 3.2f;
            Color32 wallColor = new(47, 50, 52, 255);

            var walls = new (Vector3 Offset, Vector3 Scale)[]
            {
                (new(0f, 1.538f, -2.283f), new(4.7f, 3f, 0.1f)),
                (new(0f, 1.538f, 2.283f), new(4.7f, 3f, 0.1f)),
                (new(2.283f, 1.538f, 0f), new(0.1f, 3f, 4.7f)),
                (new(-2.283f, 1.538f, 0f), new(0.1f, 3f, 4.7f)),
            };

            foreach (var (offset, scale) in walls)
            {
                var wall = Primitive.Create(
                    primitiveType: PrimitiveType.Cube,
                    flags: AdminToys.PrimitiveFlags.Visible | AdminToys.PrimitiveFlags.Collidable,
                    position: boxBase + offset,
                    rotation: Vector3.zero,
                    scale: scale,
                    spawn: true,
                    color: wallColor);

                if (wall != null)
                {
                    boxWalls.Add(wall);
                    Track(wall.GameObject);
                }
            }

            boxTop = Primitive.Create(
                primitiveType: PrimitiveType.Cube,
                flags: AdminToys.PrimitiveFlags.Visible | AdminToys.PrimitiveFlags.Collidable,
                position: boxBase + Vector3.up * 3f,
                rotation: Vector3.zero,
                scale: new Vector3(4.7f, 0.1f, 4.7f),
                spawn: true,
                color: wallColor);

            if (boxTop?.GameObject != null)
                Track(boxTop.GameObject);

            // Спуск ковчега за ~1.6с
            for (float y = -3.2f; y < 0f; y += 0.1f)
            {
                Vector3 delta = Vector3.down * (-y - 3.2f) * -1f;
                delta = Vector3.down * (3.2f - Mathf.Abs(y));
                delta = new Vector3(0f, 3.2f + y, 0f); // от -3.2 до 0

                foreach (var wall in boxWalls)
                {
                    try { wall.GameObject.transform.position += Vector3.down * -0.1f * -1f + Vector3.down * 0f; } catch { }
                }

                // Простое смещение всех стен вниз на 0.1
                foreach (var wall in boxWalls)
                {
                    try { wall.GameObject.transform.position += Vector3.down * 0.1f * 0f; } catch { }
                }

                yield return Timing.WaitForSeconds(0.05f);
            }

            // Точное позиционирование после спуска (страховка от дрейфа)
            int wi = 0;
            foreach (var (offset, _) in walls)
            {
                if (wi < boxWalls.Count && boxWalls[wi] != null)
                    boxWalls[wi].Position = boxBase + offset + Vector3.zero;
                wi++;
            }

            if (boxTop != null)
                boxTop.Position = boxBase + Vector3.up * 3f;

            yield return Timing.WaitForSeconds(1.5f);

            // 5. Вскрытие: стены разлетаются физически
            foreach (var wall in boxWalls)
            {
                try
                {
                    if (wall.GameObject == null) continue;

                    var rb = wall.GameObject.AddComponent<Rigidbody>();
                    wall.Collidable = false;
                    rb.AddExplosionForce(200f, boxBase, 10f);
                }
                catch { }
            }

            try
            {
                if (boxTop != null)
                    UnityEngine.Object.Destroy(boxTop.GameObject);
            }
            catch { }

            Timing.CallDelayed(5f, () =>
            {
                foreach (var wall in boxWalls)
                {
                    try
                    {
                        if (wall.GameObject != null)
                            UnityEngine.Object.Destroy(wall.GameObject);
                    }
                    catch { }
                }
            });

            // 6. Награда: оружие по комнате + распятие
            for (int i = 0; i < _config.LootGunsCount; i++)
            {
                try
                {
                    var gunType = PickRandomGun();
                    var gunPickup = Pickup.Create(gunType);
                    if (gunPickup != null)
                        gunPickup.Position = boxBase + new Vector3(
                            UnityEngine.Random.Range(-1.5f, 1.5f),
                            0.6f,
                            UnityEngine.Random.Range(-1.5f, 1.5f));
                }
                catch { }
            }

            // Распятие из примитивов (крест + тело-капсула)
            try
            {
                Vector3 crossPos = boxBase + Vector3.forward * 0.85f + Vector3.up * 0.7f;

                var vert = Primitive.Create(
                    primitiveType: PrimitiveType.Cube,
                    flags: AdminToys.PrimitiveFlags.Visible | AdminToys.PrimitiveFlags.Collidable,
                    position: crossPos,
                    rotation: Vector3.zero,
                    scale: new Vector3(0.14f, 1.7f, 0.14f),
                    spawn: true,
                    color: new Color32(94, 62, 34, 255));

                var horiz = Primitive.Create(
                    primitiveType: PrimitiveType.Cube,
                    flags: AdminToys.PrimitiveFlags.Visible | AdminToys.PrimitiveFlags.Collidable,
                    position: crossPos + Vector3.up * 0.35f,
                    rotation: Vector3.zero,
                    scale: new Vector3(0.9f, 0.12f, 0.12f),
                    spawn: true,
                    color: new Color32(94, 62, 34, 255));

                var body = Primitive.Create(
                    primitiveType: PrimitiveType.Capsule,
                    flags: AdminToys.PrimitiveFlags.Visible,
                    position: crossPos + Vector3.up * 0.25f,
                    rotation: Vector3.zero,
                    scale: new Vector3(0.32f, 0.55f, 0.22f),
                    spawn: true,
                    color: new Color32(214, 205, 180, 255));

                if (vert?.GameObject != null) Track(vert.GameObject);
                if (horiz?.GameObject != null) Track(horiz.GameObject);
                if (body?.GameObject != null) Track(body.GameObject);

                var crucifyLight = Light.Create(
                    position: crossPos + Vector3.up * 0.9f,
                    rotation: null,
                    scale: Vector3.one,
                    spawn: false,
                    color: new Color32(245, 187, 73, 255));

                if (crucifyLight != null)
                {
                    crucifyLight.Intensity = 5f;
                    crucifyLight.Range = 4f;
                    crucifyLight.Spawn();
                    if (crucifyLight.GameObject != null) Track(crucifyLight.GameObject);
                }
            }
            catch { }

            // 7. Финал: янтарный свет, разблокировка, снятие эффектов
            yield return Timing.WaitForSeconds(4f);

            foreach (var believer in participants)
            {
                try
                {
                    believer.DisableEffect(EffectType.SeveredHands);
                    believer.DisableEffect(EffectType.Burned);
                    believer.IsGodModeEnabled = false;
                }
                catch { }
            }

            yield return Timing.WaitForSeconds(5f);

            try
            {
                foreach (var r in Room.List)
                    r.Color = new Color32(245, 187, 73, 100);
            }
            catch { }

            foreach (var door in lockedDoors)
            {
                try { door.Unlock(); } catch { }
            }

            BroadcastNearby(roomPos, "<color=#f2d410>⛪ Призыв завершён. Да прибудет благословение...</color>");
        }
        finally
        {
            _praying = false;

            // Верующим можно снова молиться в следующем составе
            foreach (var p in Player.List.Where(x => x != null))
                p.ClearCapyHint("rag");
        }
    }

    private void CreateCandles(Vector3 roomPos)
    {
        var offsets = new[]
        {
            new Vector3(2.45f, 0.02f, 2.45f),
            new Vector3(-2.45f, 0.02f, 2.45f),
            new Vector3(-2.45f, 0.02f, -2.45f),
            new Vector3(2.45f, 0.02f, -2.45f)
        };

        foreach (var offset in offsets)
        {
            Vector3 candlePos = roomPos + offset;

            // Тело свечи (цилиндр кремовый, HDR-подобный тёплый свет сверху)
            var body = Primitive.Create(
                primitiveType: PrimitiveType.Cylinder,
                flags: AdminToys.PrimitiveFlags.Visible,
                position: candlePos,
                rotation: Vector3.zero,
                scale: new Vector3(0.07f, 0.03f, 0.07f),
                spawn: true,
                color: new Color32(214, 205, 180, 255));

            if (body?.GameObject != null)
                Track(body.GameObject);

            // Фитиль
            var wick = Primitive.Create(
                primitiveType: PrimitiveType.Capsule,
                flags: AdminToys.PrimitiveFlags.Visible,
                position: candlePos + Vector3.up * 0.03f,
                rotation: new Vector3(-6.53f, -6.6f, -5f),
                scale: new Vector3(0.005f, 0.01f, 0.005f),
                spawn: true,
                color: new Color32(13, 12, 11, 255));

            if (wick?.GameObject != null)
                Track(wick.GameObject);

            // HDR-свечение пламени
            var flame = Light.Create(
                position: candlePos + Vector3.up * 0.09f,
                rotation: null,
                scale: Vector3.one,
                spawn: false,
                color: new Color32(245, 187, 73, 255));

            if (flame != null)
            {
                flame.Intensity = 1.5f;
                flame.Range = 1f;
                flame.ShadowStrength = 0f;
                flame.Spawn();

                if (flame.GameObject != null)
                    Track(flame.GameObject);
            }
        }
    }

    private ItemType PickRandomGun()
    {
        var guns = new[]
        {
            ItemType.GunE11SR, ItemType.GunFSP9, ItemType.GunCrossvec,
            ItemType.GunRevolver, ItemType.GunShotgun, ItemType.GunAK,
            ItemType.GunCOM18, ItemType.GunLogicer
        };

        return guns[UnityEngine.Random.Range(0, guns.Length)];
    }

    private void BroadcastNearby(Vector3 pos, string message)
    {
        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, message, 5f, "rag", 22);
    }

    private void Track(GameObject go) => _ritualObjects.Add(go);
}

/// <summary>
/// .priest / .believe / .pray — команды культа.
/// </summary>
[CommandHandler(typeof(ClientCommandHandler))]
public sealed class RagnarokCommand : ICommand
{
    public string Command => "rag";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "Культ Рагнарёка";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        response = string.Empty;

        if (Player.Get(sender) is not { } player)
        {
            response = "Вы не игрок.";
            return false;
        }

        var concept = NoRulesPlugin.Instance?.Ragnarok;
        if (concept == null)
        {
            response = "Концепт выключен.";
            return false;
        }

        string sub = arguments.Count > 0 ? arguments.At(0).ToLowerInvariant() : "help";
        switch (sub)
        {
            case "priest":
            case "св":
                response = concept.TryBecomePriest(player);
                return response.StartsWith("Вы") || response.StartsWith("Уже");

            case "believe":
            case "bel":
            case "уверовать":
            {
                string err = concept.TryBelieve(player);
                response = err.Length > 0 ? err : "Вы уверовали.";
                return err.Length == 0;
            }

            case "pray":
            case "призыв":
                concept.TryPray(player);
                response = string.Empty;
                return true;

            default:
                response = "Использование: .rag <priest|believe|pray>";
                return true;
        }
    }
}
