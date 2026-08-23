using System;
using System.Globalization;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Capy.NoRules.Features;

/// <summary>
/// Система динамических хитмаркеров (всплывающий урон вокруг прицела и плашка "Убит!").
/// В точности воспроизводит поведение хитмаркеров из Hazbin.
/// </summary>
public sealed class HitmarkerFeature
{
    private readonly HitmarkerConfig _config;

    public HitmarkerFeature(HitmarkerConfig config)
    {
        _config = config;
    }

    public void OnPlayerHurting(HurtingEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Attacker == null || ev.Player == null || ev.Attacker == ev.Player)
            return;

        if (ev.Amount <= 0f || ev.Player.IsGodModeEnabled)
            return;

        // Проверка Friendly Fire
        if (!Server.FriendlyFire && ev.Attacker.Role.Side == ev.Player.Role.Side)
            return;

        float damage = (float)Math.Round(ev.Amount, 1);
        int xOffset = Random.Range(-120, 120);
        int yOffset = Random.Range(-40, 60);

        string hitText = $"<align=center><voffset={yOffset}><pos={xOffset}><b><color=#ffffff>-{damage.ToString("0.#", CultureInfo.InvariantCulture)}</color></b></pos></voffset></align>";

        ev.Attacker.ShowZoneHint(HintZone.BottomCenter, hitText, 1.5f, $"hit_{Random.Range(1, 1000)}", 20);
    }

    public void OnPlayerDied(DiedEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Attacker == null || ev.Player == null || ev.Attacker == ev.Player)
            return;

        int xOffset = Random.Range(-100, 100);
        int yOffset = Random.Range(20, 80);

        string killText = $"<align=center><voffset={yOffset}><pos={xOffset}><b><color=#f24e4e><size=26>Убит!</size></color></b></pos></voffset></align>";

        ev.Attacker.ShowZoneHint(HintZone.BottomCenter, killText, 2.5f, "kill_toast", 26);
    }
}
