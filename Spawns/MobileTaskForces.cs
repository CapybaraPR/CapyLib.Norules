using System;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Concepts;
using Capy.NoRules.Controllers;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using UnityEngine;

namespace Capy.NoRules.Spawns;

/// <summary>
/// Система спавна МОГ:
/// Волна 1: Разведгруппа (Cyan #0089c7)
/// Волна 2: Аварийный отряд со специализациями (Orange #ff8f00): Командир, Инженер, Снайпер (+25% урона), Бесшумный Снайпер (Кола), Пулеметчик (250 AHP + замедление), Врач (аптечки), Разрушитель (4 гранаты)
/// Волна 3+: Стандартные отряды МОГ (Blue #0074ff)
/// </summary>
public static class MobileTaskForces
{
    private static readonly List<string> UsedUnits = new();
    private static readonly HashSet<string> SniperPlayers = new();
    private static readonly HashSet<string> GunnerPlayers = new();
    private static readonly HashSet<string> EngineerPlayers = new();

    public static int Squads { get; private set; }

    public static void Init()
    {
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
        Exiled.Events.Handlers.Player.Hurting += OnHurting;
        Exiled.Events.Handlers.Player.Died += OnDied;
        Exiled.Events.Handlers.Player.Left += OnLeft;
        Exiled.Events.Handlers.Player.ChangingRole += OnChangingRole;
    }

    public static void Unload()
    {
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
        Exiled.Events.Handlers.Player.Hurting -= OnHurting;
        Exiled.Events.Handlers.Player.Died -= OnDied;
        Exiled.Events.Handlers.Player.Left -= OnLeft;
        Exiled.Events.Handlers.Player.ChangingRole -= OnChangingRole;

        ResetState();
    }

    private static void OnRoundStarted() => ResetState();
    private static void OnRestartingRound() => ResetState();

    private static void ResetState()
    {
        Squads = 0;
        UsedUnits.Clear();
        SniperPlayers.Clear();
        GunnerPlayers.Clear();
        EngineerPlayers.Clear();
    }

    public static void SpawnMtf()
    {
        if (ConceptsController.IsActivated || Warhead.IsDetonated ||
            NoRulesPlugin.Instance?.Hackers?.IsOmegaActive == true ||
            NoRulesPlugin.Instance?.Hackers?.IsOmegaDetonated == true)
            return;

        if ((DateTime.Now - SpawnManager.LastEnter).TotalSeconds < 30)
            return;

        SpawnManager.LastEnter = DateTime.Now;

        try { Respawn.SummonNtfChopper(); } catch { }

        Timing.CallDelayed(15f, () =>
        {
            if (Warhead.IsDetonated || !Round.IsStarted ||
                NoRulesPlugin.Instance?.Hackers?.IsOmegaActive == true ||
                NoRulesPlugin.Instance?.Hackers?.IsOmegaDetonated == true ||
                ConceptsController.IsActivated)
                return;

            var spectators = Player.List.Where(x => x.Role.Type == RoleTypeId.Spectator && !x.IsOverwatchEnabled).ToList();
            if (spectators.Count == 0)
                return;

            ShuffleList(spectators);

            // Проверка на запуск отряда CO2
            if (NoRulesPlugin.Instance?.Co2 != null && NoRulesPlugin.Instance.Co2.CheckSpawnGroup(spectators))
                return;

            Squads++;
            int count = 0;

            if (Squads == 1)
            {
                // Волна 1: Разведгруппа
                Exiled.API.Features.Cassie.Message("ATTENTION ALL PERSONNEL . DESIGNATED RECONNAISSANCE SQUAD HAS ENTERED THE FACILITY", false, false, true);

                BroadcastToAll("<color=#0089c7><b>[ ПРИБЫЛА РАЗВЕДГРУППА МОГ ]</b></color>\n<size=70%><color=#cbd5e1>Передовой отряд проник в комплекс для оценки обстановки.</color></size>");

                foreach (var pl in spectators)
                {
                    count++;
                    try
                    {
                        if (count == 1)
                            SpawnFirstOne(pl, MtfRank.Commander);
                        else if (count < 7)
                            SpawnFirstOne(pl, MtfRank.Lieutenant);
                        else
                            SpawnFirstOne(pl, MtfRank.Cadet);
                    }
                    catch { }
                }
            }
            else if (Squads == 2)
            {
                // Волна 2: Аварийный отряд
                Exiled.API.Features.Cassie.Message("ATTENTION ALL PERSONNEL . EMERGENCY RESPONSE SQUAD HAS ENTERED THE FACILITY", false, false, true);

                BroadcastToAll("<color=#ff8f00><b>[ ПРИБЫЛ АВАРИЙНЫЙ ОТРЯД МОГ ]</b></color>\n<size=70%><color=#cbd5e1>Спецподразделение повышенной готовности высадилось в комплексе.</color></size>");

                foreach (var pl in spectators)
                {
                    count++;
                    try
                    {
                        switch (count)
                        {
                            case 1: SpawnEmergencyOne(pl, EmergencyType.Commander); break;
                            case 2: SpawnEmergencyOne(pl, EmergencyType.Engineer); break;
                            case 3: SpawnEmergencyOne(pl, EmergencyType.Sniper); break;
                            case 4: SpawnEmergencyOne(pl, EmergencyType.QuietSniper); break;
                            case 5: SpawnEmergencyOne(pl, EmergencyType.Gunner); break;
                            case 6: SpawnEmergencyOne(pl, EmergencyType.Physician); break;
                            case 7: SpawnEmergencyOne(pl, EmergencyType.Destroyer); break;
                            case < 13: SpawnEmergencyOne(pl, EmergencyType.Lieutenant); break;
                            default: SpawnEmergencyOne(pl, EmergencyType.Cadet); break;
                        }
                    }
                    catch { }
                }
            }
            else
            {
                // Волна 3+: Стандартный отряд МОГ
                string[] codeNames = { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa", "Lambda", "Mu", "Nu", "Xi", "Omicron", "Pi", "Rho", "Sigma", "Tau", "Upsilon", "Phi", "Chi", "Psi", "Omega", "Nine-Tailed-Fox", "Hammer-Down" };
                string unit;
                do
                {
                    string code = codeNames[UnityEngine.Random.Range(0, codeNames.Length)];
                    int num = UnityEngine.Random.Range(1, 20);
                    unit = $"{code}-{num}";
                }
                while (UsedUnits.Contains(unit) && UsedUnits.Count < 50);

                UsedUnits.Add(unit);

                Exiled.API.Features.Cassie.Message("ATTENTION ALL PERSONNEL . DESIGNATED NINE TAILED FOX SQUAD HAS ENTERED THE FACILITY", false, false, true);

                BroadcastToAll($"<color=#0074ff><b>[ ПРИБЫЛО ПОДКРЕПЛЕНИЕ МОГ ({unit}) ]</b></color>");

                foreach (var pl in spectators)
                {
                    count++;
                    try
                    {
                        if (count == 1)
                            SpawnStandardOne(pl, MtfRank.Commander, unit);
                        else if (count < 7)
                            SpawnStandardOne(pl, MtfRank.Lieutenant, unit);
                        else
                            SpawnStandardOne(pl, MtfRank.Cadet, unit);
                    }
                    catch { }
                }
            }
        });
    }

    private static void SpawnFirstOne(Player pl, MtfRank rank)
    {
        SpawnManager.ApplySpawnProtection(pl);
        RoleTypeId role = rank switch
        {
            MtfRank.Commander => RoleTypeId.NtfCaptain,
            MtfRank.Lieutenant => RoleTypeId.NtfSergeant,
            _ => RoleTypeId.NtfPrivate
        };

        pl.Role.Set(role, SpawnReason.Respawn, RoleSpawnFlags.All);

        Timing.CallDelayed(0.3f, () =>
        {
            pl.CustomInfo = "Разведгруппа";
            string rankName = rank switch
            {
                MtfRank.Commander => "<color=#0033ff>Командир</color>",
                MtfRank.Lieutenant => "<color=#0d6fff>Сержант</color>",
                _ => "<color=#00bdff>Кадет</color>"
            };
            pl.ShowZoneHint(HintZone.TopCenter,
                $"<size=70%><color=#6f6f6f>Вы — {rankName} <color=#0089c7>разведгруппы МОГ</color>\n" +
                "Ваша задача — разведать ситуацию в комплексе.</color></size>", 10f, "mtf_first", 22);
        });
    }

    private static void SpawnEmergencyOne(Player pl, EmergencyType type)
    {
        SpawnManager.ApplySpawnProtection(pl);

        RoleTypeId role = type switch
        {
            EmergencyType.Commander => RoleTypeId.NtfCaptain,
            EmergencyType.Lieutenant => RoleTypeId.NtfSergeant,
            EmergencyType.Cadet => RoleTypeId.NtfPrivate,
            _ => RoleTypeId.NtfSpecialist
        };

        pl.Role.Set(role, SpawnReason.Respawn, RoleSpawnFlags.All);

        Timing.CallDelayed(0.3f, () =>
        {
            pl.ClearInventory();

            switch (type)
            {
                case EmergencyType.Commander:
                    pl.AddItem(ItemType.KeycardMTFCaptain);
                    pl.AddItem(ItemType.GunE11SR);
                    pl.AddItem(ItemType.SCP500);
                    pl.AddItem(ItemType.Radio);
                    pl.AddItem(ItemType.GrenadeHE);
                    pl.AddItem(ItemType.Adrenaline);
                    pl.AddItem(ItemType.Flashlight);
                    pl.AddItem(ItemType.ArmorCombat);
                    pl.CustomInfo = "Капитан | Аварийный отряд";
                    ShowEmergencyHint(pl, "<color=#0033ff>Командир</color>", "отдавать высокоуровневые приказы");
                    break;

                case EmergencyType.Engineer:
                    EngineerPlayers.Add(pl.UserId);
                    pl.AddItem(ItemType.KeycardContainmentEngineer);
                    pl.AddItem(ItemType.GunCrossvec);
                    pl.AddItem(ItemType.Adrenaline);
                    pl.AddItem(ItemType.Medkit);
                    pl.AddItem(ItemType.Radio);
                    pl.AddItem(ItemType.GrenadeFlash);
                    pl.AddItem(ItemType.Flashlight);
                    pl.AddItem(ItemType.ArmorHeavy);
                    pl.CustomInfo = "Инженер | Аварийный отряд";
                    ShowEmergencyHint(pl, "<color=#ff4640>Инженер</color>", "починить неисправности в комплексе");
                    break;

                case EmergencyType.Sniper:
                    SniperPlayers.Add(pl.UserId);
                    pl.AddItem(ItemType.KeycardMTFOperative);
                    pl.AddItem(ItemType.GunE11SR);
                    pl.AddItem(ItemType.GrenadeFlash);
                    pl.AddItem(ItemType.Radio);
                    pl.AddItem(ItemType.Adrenaline);
                    pl.AddItem(ItemType.Medkit);
                    pl.AddItem(ItemType.ArmorHeavy);
                    pl.CustomInfo = "Снайпер | Аварийный отряд";
                    ShowEmergencyHint(pl, "<color=#94ff00>Снайпер</color>", "устранить дальние цели (+25% урон E-11)");
                    break;

                case EmergencyType.QuietSniper:
                    SniperPlayers.Add(pl.UserId);
                    pl.AddItem(ItemType.KeycardMTFOperative);
                    pl.AddItem(ItemType.GunE11SR);
                    pl.AddItem(ItemType.SCP207);
                    pl.AddItem(ItemType.GrenadeFlash);
                    pl.AddItem(ItemType.Radio);
                    pl.AddItem(ItemType.Adrenaline);
                    pl.AddItem(ItemType.Medkit);
                    pl.AddItem(ItemType.ArmorHeavy);
                    pl.CustomInfo = "Бесшумный Снайпер | Аварийный отряд";
                    ShowEmergencyHint(pl, "<color=#415261>Бесшумный</color> <color=#94ff00>Снайпер</color>", "устранить цели максимально незаметно и быстро");
                    break;

                case EmergencyType.Gunner:
                    GunnerPlayers.Add(pl.UserId);
                    pl.AddItem(ItemType.KeycardMTFOperative);
                    pl.AddItem(ItemType.GunFRMG0);
                    pl.AddItem(ItemType.SCP500);
                    pl.AddItem(ItemType.Radio);
                    pl.AddItem(ItemType.Adrenaline);
                    pl.AddItem(ItemType.Medkit);
                    pl.AddItem(ItemType.Flashlight);
                    pl.AddItem(ItemType.ArmorHeavy);
                    pl.ArtificialHealth = 250f;
                    pl.MaxArtificialHealth = 250f;
                    pl.EnableEffect(EffectType.Slowness, 25);
                    pl.CustomInfo = "Пулеметчик | Аварийный отряд";
                    ShowEmergencyHint(pl, "<color=#0ac067>Пулеметчик</color>", "устранять цели на ближней дистанции (250 AHP)");
                    break;

                case EmergencyType.Physician:
                    pl.AddItem(ItemType.KeycardMTFOperative);
                    pl.AddItem(ItemType.GunE11SR);
                    pl.AddItem(ItemType.SCP500);
                    pl.AddItem(ItemType.Medkit);
                    pl.AddItem(ItemType.Medkit);
                    pl.AddItem(ItemType.Medkit);
                    pl.AddItem(ItemType.Adrenaline);
                    pl.AddItem(ItemType.Radio);
                    pl.AddItem(ItemType.ArmorCombat);
                    pl.CustomInfo = "Врач | Аварийный отряд";
                    ShowEmergencyHint(pl, "<color=#ff2222>Врач</color>", "лечить союзников на передовой");
                    break;

                case EmergencyType.Destroyer:
                    pl.AddItem(ItemType.KeycardMTFOperative);
                    pl.AddItem(ItemType.GunFRMG0);
                    pl.AddItem(ItemType.GrenadeHE);
                    pl.AddItem(ItemType.GrenadeHE);
                    pl.AddItem(ItemType.GrenadeHE);
                    pl.AddItem(ItemType.GrenadeHE);
                    pl.AddItem(ItemType.Radio);
                    pl.AddItem(ItemType.ArmorHeavy);
                    pl.CustomInfo = "Разрушитель | Аварийный отряд";
                    ShowEmergencyHint(pl, "<color=#ff3b00>Разрушитель</color>", "уничтожить цели с высоким уровнем брони (4 гранаты)");
                    break;

                case EmergencyType.Lieutenant:
                    pl.AddItem(ItemType.KeycardMTFOperative);
                    pl.AddItem(ItemType.GunE11SR);
                    pl.AddItem(ItemType.Medkit);
                    pl.AddItem(ItemType.Radio);
                    pl.AddItem(ItemType.ArmorCombat);
                    pl.CustomInfo = "Сержант | Аварийный отряд";
                    ShowEmergencyHint(pl, "<color=#0d6fff>Сержант</color>", "исполнять приказы высших по рангу");
                    break;

                default:
                    pl.AddItem(ItemType.KeycardMTFPrivate);
                    pl.AddItem(ItemType.GunCrossvec);
                    pl.AddItem(ItemType.Medkit);
                    pl.AddItem(ItemType.Radio);
                    pl.AddItem(ItemType.ArmorCombat);
                    pl.CustomInfo = "Кадет | Аварийный отряд";
                    ShowEmergencyHint(pl, "<color=#00bdff>Кадет</color>", "исполнять приказы высших по рангу");
                    break;
            }
        });
    }

    private static void SpawnStandardOne(Player pl, MtfRank rank, string unitName)
    {
        SpawnManager.ApplySpawnProtection(pl);
        RoleTypeId role = rank switch
        {
            MtfRank.Commander => RoleTypeId.NtfCaptain,
            MtfRank.Lieutenant => RoleTypeId.NtfSergeant,
            _ => RoleTypeId.NtfPrivate
        };

        pl.Role.Set(role, SpawnReason.Respawn, RoleSpawnFlags.All);
        Timing.CallDelayed(0.3f, () =>
        {
            pl.CustomInfo = unitName;
        });
    }

    private static void ShowEmergencyHint(Player pl, string roleTitle, string task)
    {
        pl.ShowZoneHint(HintZone.TopCenter,
            $"<size=70%><color=#6f6f6f>Вы — {roleTitle} <color=#ff8f00>аварийного отряда МОГ</color>\n" +
            $"Ваша задача — {task}.</color></size>", 10f, "mtf_emergency", 22);
    }

    private static void OnHurting(HurtingEventArgs ev)
    {
        if (ev.Attacker != null && SniperPlayers.Contains(ev.Attacker.UserId) && ev.DamageHandler.Type == DamageType.E11Sr)
        {
            // +25% бонусный урон снайпера с E-11
            ev.Amount *= 1.25f;
        }
    }

    private static void OnDied(DiedEventArgs ev)
    {
        if (ev.Player != null)
        {
            SniperPlayers.Remove(ev.Player.UserId);
            GunnerPlayers.Remove(ev.Player.UserId);
            EngineerPlayers.Remove(ev.Player.UserId);
        }
    }

    private static void OnLeft(LeftEventArgs ev)
    {
        if (ev.Player != null)
        {
            SniperPlayers.Remove(ev.Player.UserId);
            GunnerPlayers.Remove(ev.Player.UserId);
            EngineerPlayers.Remove(ev.Player.UserId);
        }
    }

    private static void OnChangingRole(ChangingRoleEventArgs ev)
    {
        if (ev.Player != null)
        {
            SniperPlayers.Remove(ev.Player.UserId);
            GunnerPlayers.Remove(ev.Player.UserId);
            EngineerPlayers.Remove(ev.Player.UserId);
        }
    }

    private static void BroadcastToAll(string text)
    {
        foreach (var p in Player.List.Where(x => x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, text, 6f, "mtf_spawn", 22);
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

    public enum MtfRank { Commander, Lieutenant, Cadet }
    public enum EmergencyType { Commander, Engineer, Sniper, QuietSniper, Gunner, Physician, Destroyer, Lieutenant, Cadet }
}
