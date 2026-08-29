using System;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.ServerSpecific;
using Capy.Engine.Studio.Core;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using MEC;
using UnityEngine;

namespace Capy.NoRules.Scps;

/// <summary>
/// SCP-1162 («Дыра») — аномальная щель в камере 173.
/// Игрок суёт предмет ([E] рядом с дырой) и вытягивает случайный из белого списка.
/// С пустыми руками или при неудаче (5%) — дыра «кусается»: -30 HP и эффекты.
/// </summary>
public sealed class Scp1162Feature
{
    private readonly Scp1162Config _config;
    private Vector3? _holePosition;
    private bool _enabled;

    public Scp1162Feature(Scp1162Config config)
    {
        _config = config;
    }

    public void Enable()
    {
        if (_enabled || !_config.IsEnabled) return;
        _enabled = true;

        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        AssKeybinds.OnKeybindPressed -= OnKeybindPressed;
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;

        _holePosition = null;
    }

    public void OnWaitingForPlayers() => _holePosition = null;

    public void OnRoundStarted()
    {
        _holePosition = null;

        if (!_config.IsEnabled) return;

        try
        {
            var room = Room.List.FirstOrDefault(r => r.Type == RoomType.Lcz173);
            if (room == null)
            {
                Log.Warn("[SCP-1162] Комната Lcz173 не найдена.");
                return;
            }

            var existing = SchematicLoader.SpawnedSchematics.FirstOrDefault(s =>
                s.Name.Equals(_config.SchematicName, StringComparison.OrdinalIgnoreCase));

            var schematic = existing ?? MapManager.SpawnInRoom(
                room,
                _config.SchematicName,
                new Vector3(_config.OffsetX, _config.OffsetY, _config.OffsetZ),
                Quaternion.Euler(0f, _config.RotationY, 0f));

            if (schematic != null)
                _holePosition = schematic.Position + Vector3.up * 0.4f;
            else
                Log.Warn($"[SCP-1162] Не удалось заспавнить схематику '{_config.SchematicName}'.");
        }
        catch (Exception ex)
        {
            Log.Error($"[SCP-1162] Ошибка спавна: {ex}");
        }
    }

    private bool IsNearHole(Player player)
    {
        return _holePosition.HasValue &&
               (player.Position - _holePosition.Value).sqrMagnitude <= _config.InteractRadius * _config.InteractRadius;
    }

    private void OnKeybindPressed(Player player, CustomKeybind keybind)
    {
        if (keybind != CustomKeybind.E || !_config.IsEnabled)
            return;

        TryInteract(player);
    }

    private void TryInteract(Player player)
    {
        if (_holePosition == null || player == null || !player.IsAlive || player.IsScp)
            return;

        if (!IsNearHole(player))
            return;

        // Пустые руки — дыра требует жертву
        if (player.CurrentItem == null)
        {
            ApplyBite(player, emptyHands: true,
                "<b>Вы протянули <color=red>пустую</color> руку, но <color=red>не смогли её вытянуть</color></b>");
            return;
        }

        // Шанс неудачи
        int roll = UnityEngine.Random.Range(1, 101);
        if (roll <= Mathf.Clamp(_config.FailChancePercent, 0, 100))
        {
            ApplyBite(player, emptyHands: false,
                "<b>Вы протянули руку, но <color=red>не смогли её вытянуть</color></b>");
            return;
        }

        ItemType givenType = _config.AllowedItems[UnityEngine.Random.Range(0, _config.AllowedItems.Count)];

        player.RemoveItem(player.CurrentItem);

        var given = player.AddItem(givenType);
        if (given != null)
            player.CurrentItem = given;

        player.ShowZoneHint(HintZone.Notification,
            $"<color=#c084fc>🕳️ Вы сунули руку в дыру и вытянули <b>{givenType}</b></color>", 3f, "scp1162", 22);
    }

    private void ApplyBite(Player player, bool emptyHands, string message)
    {
        try
        {
            player.EnableEffect(EffectType.Flashed, 1, 1f);
            player.EnableEffect(EffectType.SeveredHands, 0f);
            player.EnableEffect(EffectType.Traumatized, 1, 5f);
            player.Health = Mathf.Max(1f, player.Health - _config.FailDamage);
        }
        catch { }

        string extra = emptyHands ? "" : "\n<size=14><color=#ff8888>(и забрал ваш предмет... шучу. Пока что.)</color></size>";
        player.ShowZoneHint(HintZone.Notification, message + extra, 3f, "scp1162", 22);
    }
}

