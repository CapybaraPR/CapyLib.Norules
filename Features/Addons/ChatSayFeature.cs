using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using CommandSystem;
using Exiled.API.Features;
using MEC;

namespace Capy.NoRules.Features;

/// <summary>
/// Глобальный чат через .say: последние сообщения выводятся всем игрокам в HUD
/// с затуханием по времени. Анти-спам кулдаун, ограничение длины, вырезание rich-text.
/// </summary>
public sealed class ChatSayFeature
{
    private readonly ChatSayConfig _config;
    private readonly List<(string Nickname, string Message, DateTime Time)> _history = new();
    private readonly Dictionary<string, DateTime> _lastMessageTime = new();
    private static readonly Regex RichTextRegex = new(@"<.*?>", RegexOptions.Compiled);

    public ChatSayFeature(ChatSayConfig config)
    {
        _config = config;
    }

    public void OnRestartingRound()
    {
        _history.Clear();
        _lastMessageTime.Clear();
    }

    public bool TrySend(Player player, string rawMessage, out string response)
    {
        response = string.Empty;

        if (!_config.IsEnabled || player == null || !player.IsVerified)
            return false;

        string message = RichTextRegex.Replace(rawMessage ?? string.Empty, string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(message))
        {
            response = "Сообщение пустое.";
            return false;
        }

        if (message.Length > _config.MaxLength)
            message = message.Substring(0, _config.MaxLength);

        if (_lastMessageTime.TryGetValue(player.UserId, out var last) &&
            (DateTime.UtcNow - last).TotalSeconds < _config.CooldownSeconds)
        {
            response = $"Подождите {Math.Ceiling(_config.CooldownSeconds - (DateTime.UtcNow - last).TotalSeconds)} сек. перед следующим сообщением.";
            return false;
        }

        _lastMessageTime[player.UserId] = DateTime.UtcNow;
        _history.Add((player.Nickname, message, DateTime.UtcNow));

        while (_history.Count > Math.Max(1, _config.HistorySize))
            _history.RemoveAt(0);

        ShowHistory();
        return true;
    }

    /// <summary>
    /// Показывает текущую историю чата всем игрокам.
    /// </summary>
    public void ShowHistory()
    {
        if (_history.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<color=#7dd3fc><b>💬 ЧАТ КАПИБАР</b></color>");

        foreach (var (nick, msg, time) in _history.ToList())
        {
            float age = (float)(DateTime.UtcNow - time).TotalSeconds;
            float lifeLeft = _config.MessageLifetime - age;
            if (lifeLeft <= 0) continue;

            // Затухание: чем старее сообщение — тем прозрачнее/серее
            string color = lifeLeft < 3f ? "#6b7280" : lifeLeft < 6f ? "#9ca3af" : "#e5e7eb";
            sb.AppendLine($"<color={color}><color=#fbbf24>{nick}:</color> {msg}</color>");
        }

        string text = sb.ToString().TrimEnd();
        foreach (var player in Player.List.Where(p => p != null && p.IsConnected))
            player.ShowZoneHint(HintZone.UpperRight, text, 2f, "chat_say", 20);

        // Перезрисовка до истечения жизни последнего сообщения
        float ttl = _config.MessageLifetime + 0.5f;
        Timing.KillCoroutines("chat_say_refresh");
        Timing.RunCoroutine(RefreshLoop(ttl), "chat_say_refresh");
    }

    private IEnumerator<float> RefreshLoop(float totalSeconds)
    {
        float elapsed = 0f;
        while (elapsed < totalSeconds && _history.Count > 0)
        {
            yield return Timing.WaitForSeconds(2f);
            elapsed += 2f;

            if (_history.Count == 0) break;
            ShowHistorySilent();
        }
    }

    private void ShowHistorySilent()
    {
        var alive = _history.Where(h => (DateTime.UtcNow - h.Time).TotalSeconds < _config.MessageLifetime).ToList();
        if (alive.Count == 0)
        {
            foreach (var p in Player.List.Where(x => x != null && x.IsConnected))
                p.ClearCapyHint("chat_say");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<color=#7dd3fc><b>💬 ЧАТ КАПИБАР</b></color>");
        foreach (var (nick, msg, _) in alive)
            sb.AppendLine($"<color=#e5e7eb><color=#fbbf24>{nick}:</color> {msg}</color>");

        string text = sb.ToString().TrimEnd();
        foreach (var player in Player.List.Where(p => p != null && p.IsConnected))
            player.ShowZoneHint(HintZone.UpperRight, text, 2.2f, "chat_say", 20);
    }
}

/// <summary>
/// Команда .say &lt;text&gt; — сообщение в глобальный чат.
/// </summary>
[CommandHandler(typeof(ClientCommandHandler))]
public sealed class SayCommand : ICommand
{
    public string Command => "say";
    public string[] Aliases => new[] { "s" };
    public string Description => "Написать в глобальный чат сервера";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (Player.Get(sender) is not { } player)
        {
            response = "Вы не игрок.";
            return false;
        }

        var feature = NoRulesPlugin.Instance?.ChatSay;
        if (feature == null)
        {
            response = "Чат выключен на этом сервере.";
            return false;
        }

        string text = string.Join(" ", arguments.ToArray());
        if (!feature.TrySend(player, text, out string error))
        {
            response = error;
            return false;
        }

        response = string.Empty;
        return true;
    }
}
