using System;
using Capy.NoRules.Config;
using Capy.NoRules.Modules;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Capy.NoRules.Addons;

/// <summary>
/// Реалистичная вариация роста игроков (RandomScale):
/// При спавне модель масштабируется в диапазоне от MinScale до MaxScale (например, 0.95x - 1.05x).
/// Игроки перестают выглядеть как одинаковые клоны одного роста.
/// </summary>
public sealed class RandomScaleFeature
{
    private readonly RandomScaleConfig _config;

    public RandomScaleFeature(RandomScaleConfig config)
    {
        _config = config;
    }

    public void OnPlayerSpawned(SpawnedEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Player == null || !ev.Player.IsConnected)
            return;

        // Не меняем масштаб в лобби ожидания или в режиме Vanish
        if (NoRulesPlugin.Instance?.Lobby?.IsInLobby == true || VanishFeature.IsVanished(ev.Player))
        {
            ev.Player.Scale = Vector3.one;
            return;
        }

        RoleTypeId role = ev.Player.Role.Type;
        if (role == RoleTypeId.Spectator || role == RoleTypeId.None)
        {
            ev.Player.Scale = Vector3.one;
            return;
        }

        if (role == RoleTypeId.Tutorial && !_config.AffectTutorial)
        {
            ev.Player.Scale = Vector3.one;
            return;
        }

        if (ev.Player.IsScp && !_config.AffectScps)
        {
            ev.Player.Scale = Vector3.one;
            return;
        }

        float scale = Random.Range(_config.MinScale, _config.MaxScale);

        // Применяем с лёгкой задержкой после спавна, чтобы сетевая синхронизация роли не перезаписала масштаб
        Timing.CallDelayed(0.15f, () =>
        {
            if (ev.Player == null || !ev.Player.IsConnected || !ev.Player.IsAlive)
                return;

            if (NoRulesPlugin.Instance?.Lobby?.IsInLobby == true || VanishFeature.IsVanished(ev.Player))
            {
                ev.Player.Scale = Vector3.one;
                return;
            }

            ev.Player.Scale = Vector3.one * scale;
        });
    }
}
