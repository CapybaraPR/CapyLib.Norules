using System.Collections.Generic;
using Capy.NoRules.Config;
using Exiled.Events.EventArgs.Scp330;
using InventorySystem.Items.Usables.Scp330;
using UnityEngine;

namespace Capy.NoRules.Addons;

/// <summary>
/// Система спавна редкой Розовой Конфеты (Pink Candy) в SCP-330.
/// Прогрессивный шанс: каждая съеденная розовая конфета увеличивает шанс следующей
/// на ProgressiveBonusPercent (сброс при выходе игрока / рестарте раунда).
/// </summary>
public sealed class PinkCandyFeature
{
    private readonly PinkCandyConfig _config;
    private readonly Dictionary<string, int> _pinkEaten = new();

    public PinkCandyFeature(PinkCandyConfig config)
    {
        _config = config;
    }

    public void OnInteractingScp330(InteractingScp330EventArgs ev)
    {
        if (!_config.IsEnabled || !ev.IsAllowed || ev.Player == null)
            return;

        // Прогрессивный шанс: базовый + бонус за каждую ранее съеденную розовую
        _pinkEaten.TryGetValue(ev.Player.UserId, out int eaten);
        float chance = Mathf.Clamp(
            _config.PinkCandyChance + eaten * Mathf.Max(0, _config.ProgressiveBonusPercent),
            0f, 100f);

        int roll = UnityEngine.Random.Range(0, 100);
        if (roll < chance)
        {
            ev.Candy = CandyKindID.Pink;
            _pinkEaten[ev.Player.UserId] = eaten + 1;
        }
    }

    public void OnLeft(Exiled.API.Features.Player? player)
    {
        if (player != null)
            _pinkEaten.Remove(player.UserId);
    }

    public void OnRestartingRound() => _pinkEaten.Clear();
}

