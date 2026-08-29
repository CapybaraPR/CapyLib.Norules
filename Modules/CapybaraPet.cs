using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.Studio.Core;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Modules;

/// <summary>
/// Эксклюзивный летающий питомец — Королевская Капибара с золотой короной для Владельца сервера.
/// Доступна исключительно по списку OwnerSteamIds.
/// </summary>
public sealed class CapybaraPetFeature
{
    private readonly CapybaraPetConfig _config;
    private readonly ConcurrentDictionary<int, SchematicObject> _activePets = new();
    private readonly ConcurrentDictionary<int, CoroutineHandle> _activeCoroutines = new();
    private readonly List<SchematicObject> _staticCapybaras = new();

    public CapybaraPetFeature(CapybaraPetConfig config)
    {
        _config = config;
    }

    public bool IsOwner(Player? player)
    {
        if (player == null) return false;
        string rawId = player.RawUserId ?? player.UserId ?? string.Empty;
        return _config.OwnerSteamIds.Any(id => !string.IsNullOrWhiteSpace(id) && rawId.Contains(id));
    }

    public void OnPlayerSpawned(SpawnedEventArgs ev)
    {
        if (ev.Player == null || !_config.IsEnabled || !IsOwner(ev.Player)) return;

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

        foreach (var pet in _activePets.Values)
        {
            try { pet?.Destroy(); } catch { }
        }
        _activePets.Clear();

        ClearAllStaticCapybaras();
    }

    public bool TogglePet(Player player, out string response)
    {
        if (!IsOwner(player))
        {
            response = "<color=red>[ДОСТУП ЗАПРЕЩЁН]</color> Королевская капибара доступна только владельцу сервера.";
            return false;
        }

        if (_activePets.ContainsKey(player.Id))
        {
            DespawnPet(player);
            response = "<color=yellow>[КАПИБАРА]</color> Королевская капибара спрятана.";
            return true;
        }
        else
        {
            SpawnPetForPlayer(player);
            response = "<color=green>[КАПИБАРА]</color> 👑 <b>Королевская капибара</b> заспавнена рядом с тобой!";
            return true;
        }
    }

    /// <summary>
    /// Спавнит неподвижную 3D-капибару с короной прямо перед игроком для осмотра.
    /// </summary>
    public bool SpawnStaticCapybara(Player player, float scale, out string response)
    {
        if (!IsOwner(player))
        {
            response = "<color=red>[ДОСТУП ЗАПРЕЩЁН]</color> Команда доступна только владельцу сервера.";
            return false;
        }

        try
        {
            if (scale <= 0.05f) scale = 0.8f;

            Vector3 forward = player.CameraTransform != null ? player.CameraTransform.forward : player.Rotation * Vector3.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 rootPos = player.Position + (forward * 1.8f);
            Quaternion rootRot = Quaternion.LookRotation(-forward, Vector3.up);

            var schem = SchematicLoader.Spawn(_config.SchematicName, rootPos, rootRot, Vector3.one * scale);
            if (schem == null)
            {
                response = $"<color=red>[ОШИБКА]</color> Не удалось найти схематику '<b>{_config.SchematicName}</b>'.";
                return false;
            }

            lock (_staticCapybaras)
            {
                _staticCapybaras.Add(schem);
            }

            response = $"<color=green>[КАПИБАРА]</color> 👑 Королевская капибара заспавнена перед тобой (масштаб: {scale:F2})! Всего на карте: {_staticCapybaras.Count}";
            return true;
        }
        catch (Exception ex)
        {
            response = $"<color=red>[ОШИБКА]</color> Не удалось заспавнить капибару: {ex.Message}";
            return false;
        }
    }

    public int ClearAllStaticCapybaras()
    {
        int count = 0;
        lock (_staticCapybaras)
        {
            count = _staticCapybaras.Count;
            foreach (var s in _staticCapybaras)
            {
                try { s?.Destroy(); } catch { }
            }
            _staticCapybaras.Clear();
        }
        return count;
    }

    private void SpawnPetForPlayer(Player player)
    {
        try
        {
            Vector3 rootPos = player.Position + (player.Rotation * new Vector3(0.55f, 1.35f, -0.2f));
            Quaternion rootRot = player.Rotation;

            var schem = SchematicLoader.Spawn(_config.SchematicName, rootPos, rootRot, Vector3.one * _config.Scale);
            if (schem == null)
            {
                Log.Warn($"[CapybaraPet] Не удалось заспавнить схематику '{_config.SchematicName}'.");
                return;
            }

            _activePets[player.Id] = schem;
            _activeCoroutines[player.Id] = Timing.RunCoroutine(PetFollowCoroutine(player, schem));

            player.ShowZoneHint(
                HintZone.TopCenter,
                "<color=#ffa94e>👑 <b>Твоя королевская капибара рядом!</b> 🌟</color>",
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
        Vector3 currentCenter = player.Position + (player.Rotation * new Vector3(0.55f, 1.35f, -0.2f));
        Quaternion currentRotation = player.Rotation;

        while (player != null && player.IsConnected && player.IsAlive && pet != null && !pet.IsDestroyed)
        {
            if (!VanishFeature.IsVanished(player))
            {
                try
                {
                    float hover = Mathf.Sin(Time.time * 2.5f) * 0.06f;
                    Vector3 targetCenter = player.Position + (player.Rotation * new Vector3(0.55f, 1.35f + hover, -0.15f));

                    float tilt = Mathf.Sin(Time.time * 2.5f) * 3.5f;
                    Quaternion targetRot = Quaternion.Euler(0, player.Rotation.eulerAngles.y - 10f, tilt);

                    currentCenter = Vector3.Lerp(currentCenter, targetCenter, 0.32f);
                    currentRotation = Quaternion.Slerp(currentRotation, targetRot, 0.32f);

                    pet.SetTransform(currentCenter, currentRotation);
                }
                catch
                {
                    break;
                }
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
            try { pet?.Destroy(); } catch { }
        }
    }
}

