using System;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hud.Panels;
using Capy.NoRules.Controllers;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Spawns;

/// <summary>
/// Главный диспетчер спавна волн (МОГ и Повстанцев Хаоса), анти-спавнкилла и таймера подкреплений.
/// </summary>
public static class SpawnManager
{
    private static readonly HashSet<string> ProtectedPlayers = new();

    public static DateTime LastEnter { get; set; } = DateTime.MinValue;
    public static float TimeRemaining { get; private set; }
    public static string IncomingSquadName { get; private set; } = string.Empty;
    public static string IncomingSquadColor { get; private set; } = string.Empty;
    public static bool IsSquadKnown { get; private set; }

    public static void Init()
    {
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
        Exiled.Events.Handlers.Player.Hurting += OnHurting;
        Exiled.Events.Handlers.Player.Died += OnDied;
        Exiled.Events.Handlers.Player.Left += OnLeft;

        CustomRespawnTimerProvider.Provider = GetRespawnInfo;

        MobileTaskForces.Init();
        ChaosInsurgency.Init();
    }

    public static void Unload()
    {
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
        Exiled.Events.Handlers.Player.Hurting -= OnHurting;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        Exiled.Events.Handlers.Player.Left -= OnLeft;

        CustomRespawnTimerProvider.Provider = null;

        MobileTaskForces.Unload();
        ChaosInsurgency.Unload();

        Timing.KillCoroutines("SpawnManager_SpawnTeam");
        ProtectedPlayers.Clear();
    }

    public static void StopSpawning()
    {
        Timing.KillCoroutines("SpawnManager_SpawnTeam");
        TimeRemaining = 0;
        IsSquadKnown = false;
        IncomingSquadName = string.Empty;
        IncomingSquadColor = string.Empty;
    }

    private static bool GetRespawnInfo(out string text, out double secondsLeft, out string colorHex, out bool isKnown)
    {
        if (NoRulesPlugin.Instance?.Hackers?.IsOmegaActive == true ||
            NoRulesPlugin.Instance?.Hackers?.IsOmegaDetonated == true)
        {
            text = "Спавн заблокирован OMEGA боеголовкой";
            secondsLeft = -1;
            colorHex = "#ef4444";
            isKnown = true;
            return true;
        }

        if (!Round.IsStarted || Warhead.IsDetonated || ConceptsController.IsActivated)
        {
            text = string.Empty;
            secondsLeft = 0;
            colorHex = "#cbd5e1";
            isKnown = false;
            return false;
        }

        secondsLeft = Math.Max(0, TimeRemaining);
        isKnown = IsSquadKnown;
        text = string.IsNullOrEmpty(IncomingSquadName) ? "До подкрепления" : IncomingSquadName;
        colorHex = string.IsNullOrEmpty(IncomingSquadColor) ? "#cbd5e1" : IncomingSquadColor;
        return true;
    }

    private static void OnRoundStarted()
    {
        LastEnter = DateTime.MinValue;
        ProtectedPlayers.Clear();
        TimeRemaining = 0;
        IsSquadKnown = false;
        IncomingSquadName = string.Empty;
        IncomingSquadColor = string.Empty;

        Timing.KillCoroutines("SpawnManager_SpawnTeam");
        Timing.RunCoroutine(SpawnTeamCoroutine(), "SpawnManager_SpawnTeam");
    }

    private static void OnRestartingRound()
    {
        StopSpawning();
        ProtectedPlayers.Clear();
        LastEnter = DateTime.MinValue;
    }

    private static IEnumerator<float> SpawnTeamCoroutine()
    {
        while (Round.IsStarted)
        {
            if (NoRulesPlugin.Instance?.Hackers?.IsOmegaActive == true ||
                NoRulesPlugin.Instance?.Hackers?.IsOmegaDetonated == true ||
                ConceptsController.IsActivated || Warhead.IsDetonated)
            {
                yield return Timing.WaitForSeconds(2f);
                continue;
            }

            float totalDelay = UnityEngine.Random.Range(120f, 180f);
            TimeRemaining = totalDelay;
            IsSquadKnown = false;
            IncomingSquadName = string.Empty;
            IncomingSquadColor = string.Empty;

            // Отсчет пока до спавна больше 15 секунд
            while (TimeRemaining > 15f && Round.IsStarted)
            {
                // При OMEGA боеголовке спавны блокируются необратимо до конца раунда
                if (NoRulesPlugin.Instance?.Hackers?.IsOmegaActive == true ||
                    NoRulesPlugin.Instance?.Hackers?.IsOmegaDetonated == true)
                {
                    TimeRemaining = 0;
                    yield break;
                }

                // При временных концептах (CO2) или активной боеголовке прерываем только текущий отсчет волны
                if (ConceptsController.IsActivated || Warhead.IsDetonated)
                {
                    TimeRemaining = 0;
                    break;
                }

                yield return Timing.WaitForSeconds(1f);
                TimeRemaining -= 1f;
            }

            if (!Round.IsStarted || Warhead.IsDetonated || ConceptsController.IsActivated ||
                NoRulesPlugin.Instance?.Hackers?.IsOmegaActive == true ||
                NoRulesPlugin.Instance?.Hackers?.IsOmegaDetonated == true)
                continue;

            // За 15 секунд контроллер спавна точно определяет фракцию и вызывает технику!
            var spectators = Player.List.Where(x => x.Role.Type == RoleTypeId.Spectator && !x.IsOverwatchEnabled).ToList();
            if (spectators.Count == 0)
            {
                // Если спектаторов нет, ждем еще 30 сек
                TimeRemaining = 30f;
                while (TimeRemaining > 0f && Round.IsStarted)
                {
                    yield return Timing.WaitForSeconds(1f);
                    TimeRemaining -= 1f;
                }
                continue;
            }

            int rand = UnityEngine.Random.Range(0, 100);
            bool isChaos = rand < 50;

            IsSquadKnown = true;
            if (isChaos)
            {
                IncomingSquadName = "Повстанцы Хаоса";
                IncomingSquadColor = "#608F38";
                ChaosInsurgency.SpawnCI();
            }
            else
            {
                if (MobileTaskForces.Squads == 0)
                {
                    IncomingSquadName = "Разведгруппа МОГ";
                    IncomingSquadColor = "#0089c7";
                }
                else if (MobileTaskForces.Squads == 1)
                {
                    IncomingSquadName = "Аварийный отряд МОГ";
                    IncomingSquadColor = "#ff8f00";
                }
                else
                {
                    IncomingSquadName = "МОГ";
                    IncomingSquadColor = "#6D9FF7";
                }

                MobileTaskForces.SpawnMtf();
            }

            // Досчитываем оставшиеся 15 секунд
            while (TimeRemaining > 0f && Round.IsStarted)
            {
                yield return Timing.WaitForSeconds(1f);
                TimeRemaining -= 1f;
            }
        }
    }

    public static void ApplySpawnProtection(Player player)
    {
        if (player == null || !player.IsConnected) return;

        ProtectedPlayers.Add(player.UserId);
        Timing.CallDelayed(10f, () => ProtectedPlayers.Remove(player.UserId));
    }

    public static bool IsProtected(Player player)
    {
        return player != null && ProtectedPlayers.Contains(player.UserId);
    }

    private static void OnHurting(HurtingEventArgs ev)
    {
        if (ev.Player != null && IsProtected(ev.Player) && ev.DamageHandler.Type != DamageType.Warhead)
        {
            ev.IsAllowed = false;
            ev.Amount = 0f;
        }
    }

    private static void OnDied(DiedEventArgs ev)
    {
        if (ev.Player != null)
            ProtectedPlayers.Remove(ev.Player.UserId);
    }

    private static void OnLeft(LeftEventArgs ev)
    {
        if (ev.Player != null)
            ProtectedPlayers.Remove(ev.Player.UserId);
    }
}
