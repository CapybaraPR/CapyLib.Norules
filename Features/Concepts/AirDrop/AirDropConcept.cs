using System;
using System.Collections.Generic;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.API.Features.Toys;
using Exiled.API.Features.Pickups;
using MEC;
using UnityEngine;

namespace Capy.NoRules.Features.Concepts.AirDrop;

/// <summary>
/// Концепт «AirDrop»: каждые N секунд над Поверхностью пролетает красный
/// cargo-самолёт (модель из примитивов) и сбрасывает лут на парашюте.
/// </summary>
public sealed class AirDropConcept
{
    private readonly AirDropConfig _config;

    private CoroutineHandle _flightLoop;
    private bool _enabled;

    // Точки дропа на Поверхности
    private static readonly Vector3[] DropSpots =
    {
        new(39.2f, 1014f, -31.8f),
        new(-41f, 1002f, -36f),
        new(0f, 1005f, 20f),
        new(-10f, 1013f, 40f),
        new(60f, 1016f, -10f)
    };

    public AirDropConcept(AirDropConfig config)
    {
        _config = config;
    }

    public void Enable()
    {
        if (!_config.IsEnabled) return;
        _enabled = true;

        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;

        Timing.KillCoroutines("airdrop_loop");
        Timing.KillCoroutines("airdrop_flight");
    }

    private void OnRestartingRound() => Timing.KillCoroutines("airdrop_loop");

    private void OnRoundStarted()
    {
        Timing.KillCoroutines("airdrop_loop");
        _flightLoop = Timing.RunCoroutine(FlightLoop(), "airdrop_loop");
    }

    private IEnumerator<float> FlightLoop()
    {
        yield return Timing.WaitForSeconds(Mathf.Max(30f, _config.FirstDelaySeconds));

        while (Round.IsStarted)
        {
            try { FlyAndDrop(); } catch (Exception ex) { Log.Debug($"[AirDrop] {ex.Message}"); }

            yield return Timing.WaitForSeconds(Mathf.Max(30f, _config.IntervalSeconds));
        }
    }

    private void FlyAndDrop()
    {
        Vector3 start = new(-180f, 1038f, UnityEngine.Random.Range(-80f, 80f));
        Vector3 end = new(180f, 1038f, UnityEngine.Random.Range(-80f, 80f));
        float speed = 28f;

        Quaternion orientation = Quaternion.LookRotation((end - start).normalized);

        var parts = new (Vector3 Offset, Vector3 RotEuler, Vector3 Scale)[]
        {
            (Vector3.zero,       new(90f, 0f, 0f),   new(10f, 1f, 3f)),
            (new(0f, 0f, 2.3f),  new(90f, -60f, 0f), new(2f, 1f, 7f)),
            (new(0f, 0f, -2.3f), new(90f, 60f, 0f),  new(2f, 1f, 7f)),
            (new(-6f, 0f, 0f),   new(90f, 0f, 0f),   new(2f, 1f, 1f)),
            (new(6f, 0f, 0f),    new(90f, 0f, 0f),   new(2f, 2f, 1f))
        };

        // Собираем самолёт: каждый примитив запоминает локальный оффсет от носителя
        var planePrims = new List<(Primitive Prim, Vector3 LocalOffset)>();

        foreach (var (offset, rotEuler, scale) in parts)
        {
            var prim = Primitive.Create(
                primitiveType: PrimitiveType.Quad,
                flags: AdminToys.PrimitiveFlags.Visible,
                position: start + orientation * offset,
                rotation: orientation.eulerAngles + rotEuler,
                scale: scale,
                spawn: true,
                color: new Color32(160, 20, 20, 255));

            if (prim != null)
                planePrims.Add((prim, offset));
        }

        BroadcastToAll("<color=#ff6b6b>✈ Над комплексом замечен грузовой самолёт... ожидайте поставку!</color>");

        float totalDistance = Vector3.Distance(start, end);
        float flightTime = totalDistance / speed;

        Timing.RunCoroutine(FlightCoroutine(planePrims, start, end, speed, flightTime), "airdrop_flight");

        // Уборка после прилёта
        Timing.CallDelayed(flightTime + 2f, () =>
        {
            foreach (var (prim, _) in planePrims)
            {
                try
                {
                    if (prim?.GameObject != null)
                        prim.Destroy();
                }
                catch { }
            }
        });
    }

    private IEnumerator<float> FlightCoroutine(
        List<(Primitive Prim, Vector3 LocalOffset)> plane,
        Vector3 start, Vector3 end, float speed, float flightTime)
    {
        float elapsed = 0f;
        bool dropped = false;
        Vector3 current = start;
        Vector3 direction = (end - start).normalized;

        while (elapsed < flightTime)
        {
            yield return Timing.WaitForOneFrame;

            // Приблизительный шаг кадра (EXILED/MEC не даёт deltaTime напрямую)
            const float step = 0.033f;
            elapsed += step;
            current += direction * (speed * step);

            foreach (var (prim, offset) in plane)
            {
                try
                {
                    if (prim?.GameObject == null) continue;
                    prim.Position = current + Quaternion.LookRotation(direction) * offset;
                }
                catch { }
            }

            if (!dropped && elapsed >= flightTime * 0.45f)
            {
                dropped = true;
                DropCrate(current);
            }
        }
    }

    private void DropCrate(Vector3 planePos)
    {
        Vector3 spot = NearestDropSpot(planePos);

        BroadcastToAll("<color=#ffd285>📦 Поставка сброшена! Ищите на Поверхности.</color>");

        // Парашют-купол над местом дропа
        var parachute = Primitive.Create(
            primitiveType: PrimitiveType.Sphere,
            flags: AdminToys.PrimitiveFlags.Visible,
            position: spot + Vector3.up * 3f,
            rotation: Vector3.zero,
            scale: new Vector3(1.2f, 0.6f, 1.2f),
            spawn: true,
            color: new Color32(255, 107, 107, 200));

        if (parachute?.GameObject != null)
            TrackTemp(parachute.GameObject, 90f);

        // Лут: 2–4 предмета вокруг точки
        int items = UnityEngine.Random.Range(2, 5);
        for (int i = 0; i < items; i++)
        {
            try
            {
                var itemType = _config.DropItems[UnityEngine.Random.Range(0, _config.DropItems.Count)];
                var pk = Pickup.Create(itemType);
                if (pk != null)
                    pk.Position = spot + new Vector3(
                        UnityEngine.Random.Range(-1.2f, 1.2f),
                        1.2f,
                        UnityEngine.Random.Range(-1.2f, 1.2f));
            }
            catch { }
        }

        Log.Debug($"[AirDrop] Крейт сброшен на {spot}");
    }

    private readonly List<GameObject> _tempObjects = new();

    private void TrackTemp(GameObject go, float lifetime)
    {
        _tempObjects.Add(go);
        Timing.CallDelayed(lifetime, () =>
        {
            try
            {
                if (go != null)
                    UnityEngine.Object.Destroy(go);
                _tempObjects.Remove(go);
            }
            catch { }
        });
    }

    private Vector3 NearestDropSpot(Vector3 from)
    {
        Vector3 best = DropSpots[0];
        float bestDist = float.MaxValue;

        foreach (var spot in DropSpots)
        {
            float d = Vector3.Distance(from, spot);
            if (d < bestDist)
            {
                bestDist = d;
                best = spot;
            }
        }

        return best;
    }

    private void BroadcastToAll(string message)
    {
        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, message, 5f, "airdrop", 22);
    }
}
