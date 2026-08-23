using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MapEditorReborn.API.Features;
using MapEditorReborn.API.Features.Objects;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Features;

/// <summary>
/// Летающий питомец-капибара для выбранных SteamID.
/// Плавно парит рядом с правым плечом игрока с живой анимацией покачивания.
/// </summary>
public sealed class CapybaraPetFeature
{
    private readonly CapybaraPetConfig _config;
    private readonly ConcurrentDictionary<int, SchematicObject> _activePets = new();
    private readonly ConcurrentDictionary<int, CoroutineHandle> _activeCoroutines = new();

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

        // Удаляем старого питомца, если остался
        DespawnPet(ev.Player);

        if (!ev.Player.IsAlive || ev.Player.Role.Type == RoleTypeId.Spectator || ev.Player.Role.Type == RoleTypeId.None)
            return;

        // Небольшая задержка после спавна для стабилизации позиции игрока
        Timing.CallDelayed(0.5f, () =>
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

        foreach (var pet in _activePets.Values)
        {
            try
            {
                if (pet != null)
                    pet.Destroy();
            }
            catch { }
        }

        _activePets.Clear();
    }

    private void SpawnPetForPlayer(Player player)
    {
        try
        {
            Vector3 spawnPos = player.Position + (player.Rotation * new Vector3(0.55f, 1.35f, -0.2f));
            Quaternion spawnRot = player.Rotation;
            Vector3 scale = Vector3.one * _config.Scale;

            var schematic = ObjectSpawner.SpawnSchematic(_config.SchematicName, spawnPos, spawnRot, scale, null, false);
            if (schematic == null)
            {
                Log.Warn($"[CapybaraPet] Не удалось заспавнить схематику '{_config.SchematicName}'. Убедитесь, что она есть в Schematics/");
                return;
            }

            _activePets[player.Id] = schematic;
            _activeCoroutines[player.Id] = Timing.RunCoroutine(PetFollowCoroutine(player, schematic));

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

    private IEnumerator<float> PetFollowCoroutine(Player player, SchematicObject pet)
    {
        while (player != null && player.IsConnected && player.IsAlive && pet != null)
        {
            try
            {
                // Позиция: парит чуть выше и правее правого плеча
                float hoverOffset = Mathf.Sin(Time.time * 2.5f) * 0.06f;
                Vector3 shoulderOffset = player.Rotation * new Vector3(0.55f, 1.35f + hoverOffset, -0.15f);
                Vector3 targetPos = player.Position + shoulderOffset;

                // Поворот: слегка смотрит в сторону взгляда игрока с легким живым покачиванием
                float tilt = Mathf.Sin(Time.time * 2.5f) * 3f;
                Quaternion targetRot = Quaternion.Euler(0, player.Rotation.eulerAngles.y - 12f, tilt);

                // Плавное следование (Lerp / Slerp)
                pet.Position = Vector3.Lerp(pet.Position, targetPos, 0.28f);
                pet.Rotation = Quaternion.Slerp(pet.Rotation, targetRot, 0.28f);
                pet.UpdateObject();
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

        if (_activePets.TryRemove(player.Id, out var pet))
        {
            try
            {
                if (pet != null)
                    pet.Destroy();
            }
            catch { }
        }
    }
}
