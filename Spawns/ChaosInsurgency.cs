using System;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Controllers;
using Exiled.API.Enums;
using Exiled.API.Features;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Spawns;

/// <summary>
/// Система спавна Повстанцев Хаоса:
/// Волна 1: Blackout на 2 минуты (обесточивание комплекса)
/// Спавн Хакеров и Охранников Хакера (шанс 7% на каждого бойца)
/// </summary>
public static class ChaosInsurgency
{
    public static int ChaosSquads { get; private set; }
    public static int HackersSpawned { get; private set; }

    public static void Init()
    {
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
    }

    public static void Unload()
    {
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
        ResetState();
    }

    private static void OnRoundStarted() => ResetState();
    private static void OnRestartingRound() => ResetState();

    private static void ResetState()
    {
        ChaosSquads = 0;
        HackersSpawned = 0;
    }

    public static void SpawnCI()
    {
        if (ConceptsController.IsActivated || Warhead.IsDetonated ||
            NoRulesPlugin.Instance?.Hackers?.IsOmegaActive == true ||
            NoRulesPlugin.Instance?.Hackers?.IsOmegaDetonated == true)
            return;

        if ((DateTime.Now - SpawnManager.LastEnter).TotalSeconds < 30)
            return;

        var spectators = Player.List.Where(x => x.Role.Type == RoleTypeId.Spectator && !x.IsOverwatchEnabled).ToList();
        if (spectators.Count == 0)
            return;

        SpawnManager.LastEnter = DateTime.Now;

        try { Respawn.SummonChaosInsurgencyVan(); } catch { }

        Timing.CallDelayed(13f, () =>
        {
            if (Warhead.IsDetonated || !Round.IsStarted ||
                NoRulesPlugin.Instance?.Hackers?.IsOmegaActive == true ||
                NoRulesPlugin.Instance?.Hackers?.IsOmegaDetonated == true ||
                ConceptsController.IsActivated)
                return;

            spectators = Player.List.Where(x => x.Role.Type == RoleTypeId.Spectator && !x.IsOverwatchEnabled).ToList();
            if (spectators.Count == 0)
                return;

            bool isFirstWave = ChaosSquads == 0;
            ChaosSquads++;

            if (isFirstWave)
            {
                // Первая волна: Blackout на 120 секунд во всем комплексе!
                Timing.RunCoroutine(BlackoutCoroutine(120f), "ci_blackout");
                Exiled.API.Features.Cassie.Message("FACILITY POWER GRID FAILURE . SECONDARY POWER ACTIVATED", false, false, true);
                BroadcastToAll("<color=#16a34a><b>[ ПОВСТАНЦЫ ХАОСА ПРОНИКЛИ В КОМПЛЕКС ]</b></color>\n<size=70%><color=#cbd5e1>Электросеть перегружена, комплекс обесточен на 2 минуты!</color></size>");
            }
            else
            {
                BroadcastToAll("<color=#16a34a><b>[ ПРИБЫЛО ПОДКРЕПЛЕНИЕ ПОВСТАНЦЕВ ХАОСА ]</b></color>");
            }

            ShuffleList(spectators);

            foreach (var pl in spectators)
            {
                SpawnOne(pl);
            }
        });
    }

    private static IEnumerator<float> BlackoutCoroutine(float duration)
    {
        var savedColors = new Dictionary<Room, Color>();
        foreach (var r in Room.List)
        {
            if (r != null) savedColors[r] = r.Color;
        }

        foreach (var r in Room.List)
        {
            try { if (r != null) r.Color = new Color32(8, 8, 12, 255); } catch { }
        }

        yield return Timing.WaitForSeconds(duration);

        foreach (var kvp in savedColors)
        {
            try { if (kvp.Key != null) kvp.Key.Color = kvp.Value; } catch { }
        }
    }

    private static void SpawnOne(Player pl)
    {
        SpawnManager.ApplySpawnProtection(pl);

        int rand = UnityEngine.Random.Range(0, 100);

        // Шанс 7% стать Хакером (если заспавнено меньше 7)
        if (HackersSpawned < 7 && rand < 7)
        {
            HackersSpawned++;
            SpawnHacker(pl);
            return;
        }

        RoleTypeId role = RoleTypeId.ChaosRifleman;
        if (rand > 66)
            role = RoleTypeId.ChaosRepressor;
        else if (rand > 33)
            role = RoleTypeId.ChaosMarauder;

        pl.Role.Set(role, SpawnReason.Respawn, RoleSpawnFlags.All);
    }

    private static void SpawnHacker(Player hacker)
    {
        if (NoRulesPlugin.Instance?.Hackers != null)
        {
            NoRulesPlugin.Instance.Hackers.AddHacker(hacker);
        }
        else
        {
            hacker.Role.Set(RoleTypeId.ChaosRepressor, SpawnReason.Respawn, RoleSpawnFlags.All);
        }

        // Спавним Охранника Хакера из оставшихся спектаторов
        var spectators = Player.List.Where(x => x.Role.Type == RoleTypeId.Spectator && !x.IsOverwatchEnabled).ToList();
        if (spectators.Count > 0)
        {
            var guard = spectators[UnityEngine.Random.Range(0, spectators.Count)];
            SpawnHackerGuard(guard);
        }
    }

    private static void SpawnHackerGuard(Player guard)
    {
        SpawnManager.ApplySpawnProtection(guard);
        if (NoRulesPlugin.Instance?.Hackers != null)
        {
            NoRulesPlugin.Instance.Hackers.AddGuard(guard);
        }
        else
        {
            guard.Role.Set(RoleTypeId.ChaosRepressor, SpawnReason.Respawn, RoleSpawnFlags.All);
        }
    }

    private static void BroadcastToAll(string text)
    {
        foreach (var p in Player.List.Where(x => x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, text, 6f, "ci_spawn", 22);
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
}
