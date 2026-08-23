using Capy.NoRules.Config;
using Exiled.Events.EventArgs.Scp330;
using InventorySystem.Items.Usables.Scp330;
using UnityEngine;

namespace Capy.NoRules.Features;

/// <summary>
/// Система спавна редкой Розовой Конфеты (Pink Candy) в SCP-330.
/// </summary>
public sealed class PinkCandyFeature
{
    private readonly PinkCandyConfig _config;

    public PinkCandyFeature(PinkCandyConfig config)
    {
        _config = config;
    }

    public void OnInteractingScp330(InteractingScp330EventArgs ev)
    {
        if (!_config.IsEnabled || !ev.IsAllowed)
            return;

        int roll = UnityEngine.Random.Range(0, 100);
        if (roll < _config.PinkCandyChance)
        {
            ev.Candy = CandyKindID.Pink;
        }
    }
}
