using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using MEC;
using PlayerRoles;

namespace Capy.NoRules.Features;

/// <summary>
/// Динамический мониторинг выживших игроков по фракциям на экране Интеркома в комплексе.
/// </summary>
public sealed class IntercomListFeature
{
    private readonly IntercomListConfig _config;
    private CoroutineHandle _coroutineHandle;

    public IntercomListFeature(IntercomListConfig config)
    {
        _config = config;
    }

    public void OnRoundStarted()
    {
        if (!_config.IsEnabled) return;

        Timing.KillCoroutines(_coroutineHandle);
        _coroutineHandle = Timing.RunCoroutine(IntercomLoop());
    }

    public void OnRoundEnded()
    {
        Timing.KillCoroutines(_coroutineHandle);
    }

    public void OnWaitingForPlayers()
    {
        Timing.KillCoroutines(_coroutineHandle);
    }

    private IEnumerator<float> IntercomLoop()
    {
        while (Round.IsStarted)
        {
            try
            {
                var sb = new StringBuilder();

                // 1. Статус самого Интеркома
                if (Intercom.InUse && Intercom.Speaker != null)
                {
                    sb.AppendLine($"<size=32><color=#ff3333><b>ГОВОРИТ:</b> {Intercom.Speaker.Nickname}</color> ({(int)Intercom.SpeechRemainingTime}с)</size>");
                }
                else if (Intercom.RemainingCooldown > 0f)
                {
                    sb.AppendLine($"<size=30><color=#ffaa33>Перезарядка: {(int)Intercom.RemainingCooldown}с</color></size>");
                }
                else
                {
                    sb.AppendLine("<size=32><color=#a3e635><b>ИНТЕРКОМ ГОТОВ</b></color></size>");
                }

                sb.AppendLine("<size=20><color=#ffa94e>══════════════════════════</color></size>");

                // 2. Подсчёт живых по фракциям
                int classD = Player.List.Count(p => p.IsAlive && p.Role.Type == RoleTypeId.ClassD);
                int scientists = Player.List.Count(p => p.IsAlive && p.Role.Type == RoleTypeId.Scientist);
                int facilityGuards = Player.List.Count(p => p.IsAlive && p.Role.Type == RoleTypeId.FacilityGuard);
                int mtf = Player.List.Count(p => p.IsAlive && p.Role.Side == Side.Mtf && p.Role.Type != RoleTypeId.FacilityGuard);
                int chaos = Player.List.Count(p => p.IsAlive && p.Role.Side == Side.ChaosInsurgency);
                int scps = Player.List.Count(p => p.IsAlive && p.Role.Side == Side.Scp);
                int spectators = Player.List.Count(p => !p.IsAlive || p.Role.Type == RoleTypeId.Spectator);

                sb.AppendLine($"<size=22><color=#ff9933>Класс D:</color> <b>{classD}</b>  |  <color=#ffff33>Учёные:</color> <b>{scientists}</b></size>");
                sb.AppendLine($"<size=22><color=#999999>Охрана:</color> <b>{facilityGuards}</b>  |  <color=#3399ff>МОГ:</color> <b>{mtf}</b></size>");
                sb.AppendLine($"<size=22><color=#33cc33>Хаос:</color> <b>{chaos}</b>  |  <color=#ff3333>SCP:</color> <b>{scps}</b></size>");
                sb.AppendLine($"<size=18><color=#aaaaaa>Наблюдатели: {spectators}</color></size>");
                sb.AppendLine("<size=20><color=#ffa94e>══════════════════════════</color></size>");

                Intercom.DisplayText = sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                Log.Debug($"[IntercomList] Ошибка обновления дисплея: {ex.Message}");
            }

            yield return Timing.WaitForSeconds(_config.UpdateInterval);
        }
    }
}
