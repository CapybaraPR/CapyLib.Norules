using System;
using System.Collections.Generic;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Doors;
using Exiled.Events.EventArgs.Player;
using MEC;

namespace Capy.NoRules.Features;

/// <summary>
/// Система автоматического закрытия дверей после открытия игроком.
/// </summary>
public sealed class AutoDoorsFeature
{
    private readonly AutoDoorsConfig _config;
    private readonly HashSet<Door> _activeTimers = new();

    public AutoDoorsFeature(AutoDoorsConfig config)
    {
        _config = config;
    }

    public void OnInteractingDoor(InteractingDoorEventArgs ev)
    {
        if (!_config.IsEnabled || ev.Door == null || !ev.IsAllowed)
            return;

        // Если дверь закрывается или заперта — ничего не делаем
        if (ev.Door.IsOpen)
            return;

        // Проверка исключений
        if (_config.IgnoreGates && (ev.Door.Type == DoorType.GateA || ev.Door.Type == DoorType.GateB))
            return;

        if (_config.IgnoreCheckpoints && ev.Door.IsCheckpoint)
            return;

        if (ev.Door is ElevatorDoor || ev.Door.Type == DoorType.Airlock)
            return;

        // Запускаем отложенное закрытие
        Timing.CallDelayed(_config.CloseDelay, () =>
        {
            try
            {
                if (ev.Door != null && ev.Door.IsOpen && !ev.Door.IsLocked)
                {
                    ev.Door.IsOpen = false;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[AutoDoors] Ошибка при авто-закрытии двери: {ex.Message}");
            }
        });
    }
}
