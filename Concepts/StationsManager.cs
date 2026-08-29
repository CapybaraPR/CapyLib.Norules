using System;

using System.Collections.Concurrent;
using Exiled.API.Features;
using UnityEngine;

namespace Capy.NoRules.Concepts;

/// <summary>
/// Реестр интерактивных станций концептов.
/// Каждая станция: позиция + радиус + колбэк взаимодействия (нажатие [E] рядом).
/// </summary>
public static class StationsManager
{
    public sealed class Station
    {
        public string Id { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public float Radius { get; set; } = 2.5f;
        public Action<Player> OnInteract { get; set; } = _ => { };
    }

    private static readonly ConcurrentDictionary<string, Station> Stations = new();

    public static Station Register(string id, Vector3 position, float radius, Action<Player> onInteract)
    {
        var station = new Station { Id = id, Position = position, Radius = radius, OnInteract = onInteract };
        Stations[id] = station;
        return station;
    }

    public static void UpdatePosition(string id, Vector3 position)
    {
        if (Stations.TryGetValue(id, out var s))
            s.Position = position;
    }

    public static void Unregister(string id) => Stations.TryRemove(id, out _);

    public static void Clear() => Stations.Clear();

    /// <summary>
    /// Вызывает ближайшую станцию в радиусе досягаемости игрока. true — станция сработала.
    /// </summary>
    public static bool TryInvokeNearest(Player player)
    {
        if (player == null || !player.IsAlive) return false;

        Station? best = null;
        float bestDist = float.MaxValue;

        foreach (var station in Stations.Values)
        {
            float dist = (player.Position - station.Position).sqrMagnitude;
            float radius = Math.Max(0.5f, station.Radius);

            if (dist <= radius * radius && dist < bestDist)
            {
                bestDist = dist;
                best = station;
            }
        }

        if (best == null)
            return false;

        best.OnInteract(player);
        return true;
    }
}
