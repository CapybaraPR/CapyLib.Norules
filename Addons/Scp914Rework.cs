using System;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Scp914;
using PlayerRoles;
using Scp914;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Capy.NoRules.Addons;

/// <summary>
/// SCP-914 Rework:
/// Реалистичные эффекты, мутации и исцеления при обработке игроков внутри SCP-914:
/// - Rough: Сердечный приступ (CardiacArrest) + шанс мутировать в зомби SCP-049-2
/// - Coarse: Отравление газом + амнезия предметов и зрения
/// - 1:1: Размытие зрения
/// - Fine: Заряд бодрости (Invigorated) + ускорение (MovementBoost)
/// - Very Fine: Радужная эйфория, броня, буст скорости + шанс ИСЦЕЛИТЬ зомби обратно в Class-D!
/// </summary>
public sealed class Scp914ReworkFeature
{
    private readonly Scp914ReworkConfig _config;

    public Scp914ReworkFeature(Scp914ReworkConfig config)
    {
        _config = config;
    }

    public void OnUpgradingPlayer(UpgradingPlayerEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Player == null || !ev.Player.IsConnected || !ev.Player.IsAlive)
            return;

        // Основные SCP (кроме зомби 049-2) не модифицируются, если не включено в конфиге
        if (ev.Player.IsScp && ev.Player.Role.Type != RoleTypeId.Scp0492 && !_config.AffectMainScps)
            return;

        switch (ev.KnobSetting)
        {
            case Scp914KnobSetting.Rough:
            {
                if (ev.Player.Role.Type != RoleTypeId.Scp0492 && !ev.Player.IsScp &&
                    Random.Range(0, 100) < _config.ZombieMutationChance)
                {
                    ev.Player.Role.Set(RoleTypeId.Scp0492, SpawnReason.Respawn, RoleSpawnFlags.All);
                    ev.Player.ShowZoneHint(HintZone.Notification,
                        "<color=#ef4444>🧟 <b>Аномальная мутация в SCP-914! Вы превратились в зомби (SCP-049-2)!</b></color>",
                        4.5f, "scp914_rough", 25);
                    return;
                }

                ev.Player.EnableEffect(EffectType.CardiacArrest, 5, 10f);
                ev.Player.ShowZoneHint(HintZone.Notification,
                    "<color=#ef4444>⚙️ <b>SCP-914 [Rough]: Остановка сердца!</b></color>",
                    3.0f, "scp914_rough", 25);
                break;
            }

            case Scp914KnobSetting.Coarse:
            {
                ev.Player.EnableEffect(EffectType.Poisoned, 10, 2.5f);
                ev.Player.EnableEffect(EffectType.AmnesiaItems, 200, 10f);
                ev.Player.EnableEffect(EffectType.AmnesiaVision, 200, 10f);
                ev.Player.ShowZoneHint(HintZone.Notification,
                    "<color=#f97316>⚙️ <b>SCP-914 [Coarse]: Отравление и амнезия!</b></color>",
                    3.0f, "scp914_coarse", 25);
                break;
            }

            case Scp914KnobSetting.OneToOne:
            {
                ev.Player.EnableEffect(EffectType.Blurred, 10, 5f);
                ev.Player.ShowZoneHint(HintZone.Notification,
                    "<color=#eab308>⚙️ <b>SCP-914 [1:1]: Искажение реальности...</b></color>",
                    2.5f, "scp914_1to1", 25);
                break;
            }

            case Scp914KnobSetting.Fine:
            {
                ev.Player.EnableEffect(EffectType.Invigorated, 100, 10f);
                ev.Player.EnableEffect(EffectType.MovementBoost, 5, 10f);
                ev.Player.ShowZoneHint(HintZone.Notification,
                    "<color=#22c55e>⚙️ <b>SCP-914 [Fine]: Прилив энергии и ускорение!</b></color>",
                    3.0f, "scp914_fine", 25);
                break;
            }

            case Scp914KnobSetting.VeryFine:
            {
                if (ev.Player.Role.Type == RoleTypeId.Scp0492 && Random.Range(0, 100) < _config.ZombieCureChance)
                {
                    ev.Player.Role.Set(RoleTypeId.ClassD, SpawnReason.Respawn, RoleSpawnFlags.All);
                    ev.Player.ShowZoneHint(HintZone.Notification,
                        "<color=#38bdf8>✨ <b>ЧУДО! SCP-914 полностью излечил инфекцию — вы снова человек (Class-D)!</b></color>",
                        5.0f, "scp914_cure", 25);
                    return;
                }

                ev.Player.EnableEffect(EffectType.RainbowTaste, 1, 10f);
                ev.Player.EnableEffect(EffectType.Invigorated, 100, 10f);
                ev.Player.EnableEffect(EffectType.MovementBoost, 5, 10f);
                ev.Player.EnableEffect(EffectType.BodyshotReduction, 10, 15f);
                ev.Player.ShowZoneHint(HintZone.Notification,
                    "<color=#a855f7>✨ <b>SCP-914 [Very Fine]: Радужная эйфория, броня и максимальный буст!</b></color>",
                    3.5f, "scp914_veryfine", 25);
                break;
            }
        }
    }
}
