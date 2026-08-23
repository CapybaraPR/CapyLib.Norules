using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;

namespace Capy.NoRules.Features;

/// <summary>
/// Запись одного попадания в потоке урона.
/// </summary>
public sealed class DamageHit
{
    public float Amount { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Активный поток урона игрока по цели (волновой список хитов сверху вниз под прицелом).
/// </summary>
public sealed class DamageStream
{
    public int TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public string TargetColor { get; set; } = "#ff4444";
    public List<DamageHit> Hits { get; } = new();
    public CoroutineHandle Coroutine { get; set; }
}

/// <summary>
/// Система динамических хитмаркеров (волновой вертикальный поток урона под прицелом).
/// Воспроизводит точный стиль индикации урона из AspectLib:
/// Имя/роль цели сверху и ниспадающие цифры -XX.X волной вниз.
/// </summary>
public sealed class HitmarkerFeature
{
    private readonly HitmarkerConfig _config;
    private static readonly ConcurrentDictionary<int, DamageStream> ActiveStreams = new();

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

        int attackerId = ev.Attacker.Id;
        int targetId = ev.Player.Id;
        float damage = (float)Math.Round(ev.Amount, 1);

        var stream = ActiveStreams.GetOrAdd(attackerId, _ => new DamageStream());

        lock (stream)
        {
            // Если цель сменилась или поток устарел — сбрасываем список
            if (stream.TargetId != targetId)
            {
                stream.Hits.Clear();
                stream.TargetId = targetId;
                stream.TargetName = ev.Player.Role.Name;
                stream.TargetColor = GetSideColor(ev.Player.Role.Side);
            }

            stream.Hits.Add(new DamageHit
            {
                Amount = damage,
                Timestamp = DateTime.UtcNow
            });

            // Ограничиваем историю последними 6 попаданиями
            if (stream.Hits.Count > 6)
                stream.Hits.RemoveAt(0);

            Timing.KillCoroutines(stream.Coroutine);

            // Рендерим вертикальную волну
            RenderStream(ev.Attacker, stream);

            // Таймер автоматического затухания и очистки через 1.8с
            stream.Coroutine = Timing.CallDelayed(1.8f, () =>
            {
                lock (stream)
                {
                    stream.Hits.Clear();
                    stream.TargetId = -1;
                    ev.Attacker.ClearCapyHint("hitmarker_stream");
                }
            });
        }
    }

    public void OnPlayerDied(DiedEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Attacker == null || ev.Player == null || ev.Attacker == ev.Player)
            return;

        int attackerId = ev.Attacker.Id;
        if (ActiveStreams.TryGetValue(attackerId, out var stream))
        {
            lock (stream)
            {
                Timing.KillCoroutines(stream.Coroutine);
                stream.Hits.Clear();
                stream.TargetId = -1;
            }
        }

        string killText = $"<size=26><b><color=#f24e4e>УБИТ!</color></b></size>\n<size=18><color=#c2c2c2>{ev.Player.Nickname}</color></size>";
        ev.Attacker.ShowZoneHint(HintZone.BottomCenter, killText, 2.2f, "hitmarker_stream", 24);
    }

    private static void RenderStream(Player attacker, DamageStream stream)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<size=22><b><color={stream.TargetColor}>{stream.TargetName}</color></b></size>");

        foreach (var hit in stream.Hits)
        {
            sb.AppendLine($"<size=20><color=#ffffff>-{hit.Amount.ToString("0.#", CultureInfo.InvariantCulture)}</color></size>");
        }

        attacker.ShowZoneHint(HintZone.BottomCenter, sb.ToString().TrimEnd(), 2.0f, "hitmarker_stream", 20);
    }

    private static string GetSideColor(Exiled.API.Enums.Side side)
    {
        return side switch
        {
            Exiled.API.Enums.Side.Scp => "#ff2222",
            Exiled.API.Enums.Side.Mtf => "#6d9ff7",
            Exiled.API.Enums.Side.ChaosInsurgency => "#608f38",
            _ => "#ffa94e"
        };
    }
}
