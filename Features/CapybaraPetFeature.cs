using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AdminToys;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Toys;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Features;

/// <summary>
/// Описание одного элемента 3D-модели капибары.
/// </summary>
public struct CapyBlockDef
{
    public PrimitiveType Type;
    public Vector3 LocalPos;
    public Vector3 LocalRot;
    public Vector3 LocalScale;
    public Color Color;

    public CapyBlockDef(PrimitiveType type, Vector3 pos, Vector3 rot, Vector3 scale, string hexColor)
    {
        Type = type;
        LocalPos = pos;
        LocalRot = rot;
        LocalScale = scale;
        Color = ColorUtility.TryParseHtmlString(hexColor, out var c) ? c : Color.white;
    }
}

/// <summary>
/// Летающий питомец-капибара для выбранных SteamID.
/// Построена на нативных EXILED AdminToys (22 блока, апельсинка на голове 🍊).
/// Парит рядом с правым плечом владельца с плавной анимацией покачивания.
/// </summary>
public sealed class CapybaraPetFeature
{
    private readonly CapybaraPetConfig _config;
    private readonly ConcurrentDictionary<int, List<Primitive>> _activePets = new();
    private readonly ConcurrentDictionary<int, CoroutineHandle> _activeCoroutines = new();

    private static readonly List<CapyBlockDef> ModelBlocks = new()
    {
        // Подстилка
        new(PrimitiveType.Cylinder, new(0f, 0.01f, 0f), new(0, 0, 0), new(0.95f, 0.02f, 1.25f), "#3A5F3AEE"),
        
        // Тело
        new(PrimitiveType.Cube, new(0f, 0.38f, 0f), new(0, 0, 0), new(0.58f, 0.44f, 0.88f), "#825228"),
        new(PrimitiveType.Cylinder, new(0f, 0.52f, -0.05f), new(90, 0, 0), new(0.52f, 0.22f, 0.72f), "#794B24"),
        new(PrimitiveType.Cube, new(0f, 0.32f, 0.02f), new(0, 0, 0), new(0.59f, 0.26f, 0.78f), "#9E6738"),

        // Голова и мордочка
        new(PrimitiveType.Cube, new(0f, 0.56f, 0.48f), new(-5, 0, 0), new(0.42f, 0.38f, 0.46f), "#825228"),
        new(PrimitiveType.Cube, new(0f, 0.50f, 0.70f), new(0, 0, 0), new(0.38f, 0.28f, 0.24f), "#6A3E18"),
        new(PrimitiveType.Cube, new(0f, 0.54f, 0.825f), new(0, 0, 0), new(0.24f, 0.12f, 0.04f), "#2B1609"),
        new(PrimitiveType.Cube, new(-0.065f, 0.535f, 0.846f), new(0, 0, 0), new(0.04f, 0.04f, 0.02f), "#110A04"),
        new(PrimitiveType.Cube, new(0.065f, 0.535f, 0.846f), new(0, 0, 0), new(0.04f, 0.04f, 0.02f), "#110A04"),

        // Глаза и блики
        new(PrimitiveType.Sphere, new(-0.215f, 0.62f, 0.56f), new(0, 0, 0), new(0.075f, 0.075f, 0.075f), "#151515"),
        new(PrimitiveType.Sphere, new(0.215f, 0.62f, 0.56f), new(0, 0, 0), new(0.075f, 0.075f, 0.075f), "#151515"),
        new(PrimitiveType.Sphere, new(-0.228f, 0.642f, 0.58f), new(0, 0, 0), new(0.025f, 0.025f, 0.025f), "#FFFFFF"),
        new(PrimitiveType.Sphere, new(0.228f, 0.642f, 0.58f), new(0, 0, 0), new(0.025f, 0.025f, 0.025f), "#FFFFFF"),

        // Ушки
        new(PrimitiveType.Sphere, new(-0.20f, 0.74f, 0.38f), new(0, -20, -25), new(0.10f, 0.09f, 0.06f), "#542F12"),
        new(PrimitiveType.Sphere, new(0.20f, 0.74f, 0.38f), new(0, 20, 25), new(0.10f, 0.09f, 0.06f), "#542F12"),

        // 4 лапки
        new(PrimitiveType.Cylinder, new(-0.21f, 0.13f, 0.28f), new(0, 0, 0), new(0.15f, 0.12f, 0.15f), "#6B3E1A"),
        new(PrimitiveType.Cylinder, new(0.21f, 0.13f, 0.28f), new(0, 0, 0), new(0.15f, 0.12f, 0.15f), "#6B3E1A"),
        new(PrimitiveType.Cylinder, new(-0.21f, 0.13f, -0.28f), new(0, 0, 0), new(0.16f, 0.12f, 0.16f), "#6B3E1A"),
        new(PrimitiveType.Cylinder, new(0.21f, 0.13f, -0.28f), new(0, 0, 0), new(0.16f, 0.12f, 0.16f), "#6B3E1A"),

        // 🍊 Апельсинка на голове
        new(PrimitiveType.Sphere, new(0f, 0.81f, 0.48f), new(0, 0, 0), new(0.18f, 0.15f, 0.18f), "#FF7A00"),
        new(PrimitiveType.Cylinder, new(0f, 0.895f, 0.48f), new(0, 0, 0), new(0.02f, 0.02f, 0.02f), "#3D250D"),
        new(PrimitiveType.Cube, new(0.028f, 0.902f, 0.495f), new(15, 35, 20), new(0.065f, 0.015f, 0.04f), "#348C31")
    };

    public CapybaraPetFeature(CapybaraPetConfig config)
    {
        _config = config;
    }

    private bool IsPetOwner(Player? player)
    {
        if (player == null || !_config.IsEnabled) return false;

        string rawId = player.RawUserId ?? player.UserId ?? string.Empty;
        return _config.OwnerSteamIds.Any(id => !string.IsNullOrWhiteSpace(id) && rawId.Contains(id));
    }

    public void OnPlayerSpawned(SpawnedEventArgs ev)
    {
        if (ev.Player == null || !IsPetOwner(ev.Player)) return;

        DespawnPet(ev.Player);

        if (!ev.Player.IsAlive || ev.Player.Role.Type == RoleTypeId.Spectator || ev.Player.Role.Type == RoleTypeId.None)
            return;

        Timing.CallDelayed(0.6f, () =>
        {
            if (ev.Player == null || !ev.Player.IsConnected || !ev.Player.IsAlive)
                return;

            SpawnPetForPlayer(ev.Player);
        });
    }

    public void OnPlayerDeath(DiedEventArgs ev)
    {
        if (ev.Player != null)
            DespawnPet(ev.Player);
    }

    public void OnPlayerLeft(LeftEventArgs ev)
    {
        if (ev.Player != null)
            DespawnPet(ev.Player);
    }

    public void OnRoundRestarted()
    {
        foreach (var handle in _activeCoroutines.Values)
            Timing.KillCoroutines(handle);

        _activeCoroutines.Clear();

        foreach (var primitives in _activePets.Values)
        {
            foreach (var p in primitives)
            {
                try { p?.Destroy(); } catch { }
            }
        }

        _activePets.Clear();
    }

    private void SpawnPetForPlayer(Player player)
    {
        try
        {
            float scale = _config.Scale;
            Vector3 rootPos = player.Position + (player.Rotation * new Vector3(0.55f, 1.35f, -0.2f));
            Quaternion rootRot = player.Rotation;

            var primitives = new List<Primitive>(ModelBlocks.Count);

            foreach (var def in ModelBlocks)
            {
                Vector3 worldPos = rootPos + (rootRot * (def.LocalPos * scale));
                Quaternion worldRot = rootRot * Quaternion.Euler(def.LocalRot);
                Vector3 worldScale = def.LocalScale * scale;

                var prim = Primitive.Create(
                    primitiveType: def.Type,
                    flags: PrimitiveFlags.Visible,
                    position: worldPos,
                    rotation: worldRot.eulerAngles,
                    scale: worldScale,
                    spawn: true,
                    color: def.Color
                );

                if (prim != null)
                {
                    primitives.Add(prim);
                }
            }

            _activePets[player.Id] = primitives;
            _activeCoroutines[player.Id] = Timing.RunCoroutine(PetFollowCoroutine(player, primitives));

            player.ShowZoneHint(
                HintZone.TopCenter,
                "<color=#ffa94e>🦫 <b>Твоя капибара рядом!</b> 🍊</color>",
                3.5f,
                "capy_pet_spawn",
                22
            );
        }
        catch (Exception ex)
        {
            Log.Error($"[CapybaraPet] Ошибка при спавне капибары: {ex}");
        }
    }

    private IEnumerator<float> PetFollowCoroutine(Player player, List<Primitive> primitives)
    {
        float scale = _config.Scale;

        Vector3 currentCenter = player.Position + (player.Rotation * new Vector3(0.55f, 1.35f, -0.2f));
        Quaternion currentRotation = player.Rotation;

        while (player != null && player.IsConnected && player.IsAlive && primitives.Count > 0)
        {
            try
            {
                // Целевая позиция: парит чуть выше и правее правого плеча с покачиванием
                float hover = Mathf.Sin(Time.time * 2.5f) * 0.06f;
                Vector3 targetCenter = player.Position + (player.Rotation * new Vector3(0.55f, 1.35f + hover, -0.15f));

                float tilt = Mathf.Sin(Time.time * 2.5f) * 3.5f;
                Quaternion targetRot = Quaternion.Euler(0, player.Rotation.eulerAngles.y - 10f, tilt);

                // Плавное следование
                currentCenter = Vector3.Lerp(currentCenter, targetCenter, 0.32f);
                currentRotation = Quaternion.Slerp(currentRotation, targetRot, 0.32f);

                // Обновляем все 22 части относительно нового центра
                for (int i = 0; i < primitives.Count && i < ModelBlocks.Count; i++)
                {
                    var prim = primitives[i];
                    if (prim == null) continue;

                    var def = ModelBlocks[i];
                    prim.Position = currentCenter + (currentRotation * (def.LocalPos * scale));
                    prim.Rotation = currentRotation * Quaternion.Euler(def.LocalRot);
                }
            }
            catch
            {
                break;
            }

            yield return Timing.WaitForOneFrame;
        }

        DespawnPet(player);
    }

    private void DespawnPet(Player? player)
    {
        if (player == null) return;

        if (_activeCoroutines.TryRemove(player.Id, out var handle))
            Timing.KillCoroutines(handle);

        if (_activePets.TryRemove(player.Id, out var primitives))
        {
            foreach (var p in primitives)
            {
                try { p?.Destroy(); } catch { }
            }
        }
    }
}
