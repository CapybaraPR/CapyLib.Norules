using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using CommandSystem;
using Exiled.API.Features;
using MEC;

namespace Capy.NoRules.Addons;

/// <summary>
/// Голосование за рестарт раунда: первый игрок командой .vote открывает голосование,
/// остальные голосуют тоже .vote. По истечении времени при большинстве «за» — рестарт.
/// </summary>
public sealed class CallVoteFeature
{
    private readonly CallVoteConfig _config;

    private readonly ConcurrentDictionary<string, bool> _votes = new(); // userId -> голосовал
    private DateTime _voteEndTime = DateTime.MinValue;
    private DateTime _nextVoteAllowedTime = DateTime.MinValue;
    private CoroutineHandle _announceLoop;
    private bool _voteActive;

    public CallVoteFeature(CallVoteConfig config)
    {
        _config = config;
    }

    public void OnRestartingRound()
    {
        EndVote(silent: true);
        _nextVoteAllowedTime = DateTime.MinValue;
    }

    public void TryVote(Player player)
    {
        if (!_config.IsEnabled || player == null || !player.IsVerified || player.IsNPC)
            return;

        if (_voteActive)
        {
            if (!_votes.TryAdd(player.UserId, true))
            {
                player.ShowZoneHint(HintZone.Notification, "<color=#facc15>Вы уже проголосовали.</color>", 2f, "callvote", 22);
                return;
            }

            BroadcastStatus($"<color=#a3e635>{player.Nickname}</color> проголосовал за рестарт!");
            return;
        }

        // Попытка старта нового голосования
        if (DateTime.UtcNow < _nextVoteAllowedTime)
        {
            player.ShowZoneHint(HintZone.Notification,
                $"<color=#f87171>Голосование доступно через {(int)Math.Ceiling((_nextVoteAllowedTime - DateTime.UtcNow).TotalSeconds)} сек.</color>",
                2.5f, "callvote", 22);
            return;
        }

        int aliveCount = Player.List.Count(p => p is { IsAlive: true, IsNPC: false });
        if (aliveCount < Math.Max(2, _config.MinimumAlivePlayers))
        {
            player.ShowZoneHint(HintZone.Notification,
                $"<color=#f87171>Для голосования нужно минимум {_config.MinimumAlivePlayers} живых игроков (сейчас {aliveCount}).</color>",
                3f, "callvote", 22);
            return;
        }

        StartVote(player, aliveCount);
    }

    private void StartVote(Player starter, int aliveCount)
    {
        _votes.Clear();
        _votes[starter.UserId] = true;
        _voteActive = true;
        _voteEndTime = DateTime.UtcNow.AddSeconds(Math.Max(5f, _config.DurationSeconds));

        BroadcastStatus($"<color=#ffd285>{starter.Nickname}</color> запустил голосование за РЕСТАРТ раунда!");

        float duration = (float)(_voteEndTime - DateTime.UtcNow).TotalSeconds;
        _announceLoop = Timing.RunCoroutine(VoteLoop(duration));
    }

    private IEnumerator<float> VoteLoop(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && _voteActive)
        {
            yield return Timing.WaitForSeconds(1f);
            elapsed += 1f;

            if (!_voteActive) yield break;

            int remaining = Math.Max(0, (int)(_voteEndTime - DateTime.UtcNow).TotalSeconds);
            ShowVoteHud(remaining);

            if (remaining <= 0)
                break;
        }

        FinishVote();
    }

    private void FinishVote()
    {
        int yes = _votes.Count;
        int alive = Player.List.Count(p => p is { IsAlive: true, IsNPC: false });
        bool passed = yes > alive / 2;

        EndVote(silent: true);
        _nextVoteAllowedTime = DateTime.UtcNow.AddSeconds(Math.Max(10f, _config.CooldownSeconds));

        string result = passed
            ? $"<color=#a3e635><b>Голосование пройдено ({yes}/{alive})! Рестарт раунда...</b></color>"
            : $"<color=#f87171>Голосование не прошло ({yes}/{alive}). Нужно большинство живых игроков.</color>";

        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, result, 5f, "callvote_result", 24);

        if (passed)
        {
            Timing.CallDelayed(3f, () =>
            {
                try { Round.Restart(); }
                catch (Exception ex) { Log.Error($"[CallVote] Ошибка рестарта: {ex.Message}"); }
            });
        }
    }

    private void EndVote(bool silent)
    {
        _voteActive = false;
        _votes.Clear();
        if (_announceLoop.IsRunning)
            Timing.KillCoroutines(_announceLoop);

        if (!silent)
            return;

        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ClearCapyHint("callvote");
    }

    private void BroadcastStatus(string line)
    {
        int remaining = Math.Max(0, (int)(_voteEndTime - DateTime.UtcNow).TotalSeconds);
        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, $"{line}\n<size=20><color=#c2c2c2>Голосов: {_votes.Count}. Голосуй: <b>.vote</b>. Осталось: {remaining}с</color></size>", 4f, "callvote", 22);
    }

    private void ShowVoteHud(int remaining)
    {
        int alive = Player.List.Count(p => p is { IsAlive: true, IsNPC: false });
        string text = $"<color=#ffd285><b>🗳 ГОЛОСОВАНИЕ: рестарт раунда</b></color>\n" +
                      $"За: <color=#a3e635>{_votes.Count}</color> / нужно >{alive / 2} (живых: {alive})\n" +
                      $"<color=#c2c2c2>Голосуй: <b>.vote</b> • Осталось: {remaining}с</color>";

        foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
            p.ShowZoneHint(HintZone.TopCenter, text, 1.6f, "callvote", 20);
    }
}

/// <summary>
/// .vote — проголосовать за рестарт / начать голосование.
/// </summary>
[CommandHandler(typeof(ClientCommandHandler))]
public sealed class VoteCommand : ICommand
{
    public string Command => "vote";
    public string[] Aliases => new[] { "votereset", "голосование" };
    public string Description => "Голосование за рестарт раунда";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (Player.Get(sender) is not { } player)
        {
            response = "Вы не игрок.";
            return false;
        }

        NoRulesPlugin.Instance?.CallVote?.TryVote(player);
        response = string.Empty;
        return true;
    }
}

