using System;
using System.Globalization;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.API.Features.DamageHandlers;
using Exiled.Events.EventArgs.Player;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Capy.NoRules.Features;

/// <summary>
/// Модуль хитмаркеров, портированный 1:1 из AspectLib.Modules.HitMarker.HitMarkerModule.
/// При попадании — рандомно позиционированный урон вокруг прицела.
/// Хедшот — красный, крупный.
/// Убийство — плашка "Убит!".
/// </summary>
public sealed class HitmarkerFeature
{
    private readonly HitmarkerConfig _config;

    public HitmarkerFeature(HitmarkerConfig config)
    {
        _config = config;
    }

    private static bool IsTeammate(Player attacker, Player victim)
    {
        if (attacker == null || victim == null || attacker == victim)
            return true;

        if (Server.FriendlyFire)
            return false;

        if (attacker.Role.Team == victim.Role.Team
            && attacker.Role.Team != PlayerRoles.Team.OtherAlive
            && attacker.Role.Team != PlayerRoles.Team.Dead)
            return true;

        if (attacker.Role.Side == victim.Role.Side
            && attacker.Role.Side != Exiled.API.Enums.Side.None)
            return true;

        return false;
    }

    public void OnPlayerHurting(HurtingEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Attacker == null || ev.Player == null || ev.Attacker == ev.Player)
            return;

        if (IsTeammate(ev.Attacker, ev.Player))
            return;

        if (ev.Amount <= 0f || ev.Player.IsGodModeEnabled)
            return;

        float exactDamage = (float)Math.Round(ev.Amount, 1);
        string formattedDamage = exactDamage.ToString("0.#", CultureInfo.InvariantCulture);

        bool isHeadshot = ev.DamageHandler.BaseIs(out FirearmDamageHandler firearmHandler)
                          && firearmHandler.Hitbox == HitboxType.Headshot;

        string damageText = isHeadshot
            ? $"<b><color=#FF2222>-{formattedDamage}</color></b>"
            : $"<b>-{formattedDamage}</b>";

        Vector2 randomOffset = new Vector2(Random.Range(-350, 350), Random.Range(250, 450));

        ev.Attacker.ShowHint(
            damageText,
            randomOffset,
            2.5f,
            HintVerticalAlign.Middle,
            HintAlignment.Center,
            isHeadshot ? 16 : 14,
            "hit"
        );
    }

    public void OnPlayerDied(DiedEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Attacker == null || ev.Player == null || ev.Attacker == ev.Player)
            return;

        if (IsTeammate(ev.Attacker, ev.Player))
            return;

        string killText = "<b><color=#f24e4e>Убит!</color></b>";
        Vector2 randomOffset = new Vector2(Random.Range(-350, 350), Random.Range(400, 500));

        ev.Attacker.ShowHint(
            killText,
            randomOffset,
            4.0f,
            HintVerticalAlign.Middle,
            HintAlignment.Center,
            24,
            "kill"
        );
    }
}
