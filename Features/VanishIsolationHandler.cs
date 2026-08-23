using System;
using System.Linq;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Roles;
using Exiled.Events.EventArgs.Player;
using Exiled.Events.EventArgs.Scp049;
using Exiled.Events.EventArgs.Scp096;
using Exiled.Events.EventArgs.Scp173;
using Exiled.Events.EventArgs.Scp3114;
using Exiled.Events.EventArgs.Scp330;
using Exiled.Events.EventArgs.Scp914;
using Exiled.Events.EventArgs.Warhead;
using UnityEngine;

namespace Capy.NoRules.Features;

/// <summary>
/// Обработчик полной физической, сетевой и игровой изоляции свободного наблюдателя:
/// 1. Физическая прозрачность: отключение всех Unity-коллайдеров (пули и рейкасты пролетают насквозь).
/// 2. Сетевая маскировка: GhostMode (FpcRole.IsInvisible) + Mirror ChangeAppearance (Spectator).
/// 3. Игровая изоляция: блокировка агра всех SCP (049, 106, 173, 096, 3114) и игнор Tesla.
/// 4. Блокировка взаимодействия: запрет на нажатие любых кнопок, дверей, верстаков, боеголовки и 914.
/// </summary>
public static class VanishIsolationHandler
{
    public static void ApplyIsolation(Player player)
    {
        if (player == null || !player.IsConnected) return;

        // 1. Отключаем все коллайдеры и переводим в Ignore Raycast — пули летят насквозь
        try
        {
            foreach (var collider in player.GameObject.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            player.GameObject.layer = 2; // Ignore Raycast
        }
        catch { }

        // 2. Сетевой GhostMode — сервер не шлет данные о позиции игрока
        if (player.Role.Is(out FpcRole fpcRole))
        {
            fpcRole.IsInvisible = true;
        }

        // 3. Сетевая подмена внешности
        Capy.Core.Extensions.NetworkExtensions.ChangeAppearance(player, PlayerRoles.RoleTypeId.Spectator, true);

        // 4. Бессмертие, выключение байпаса и мут микрофона для живых
        player.IsGodModeEnabled = true;
        player.IsBypassModeEnabled = false;
        player.IsMuted = true;
        player.IsNoclipPermitted = true;
        player.IsNoclipEnabled = true;
    }

    public static void RemoveIsolation(Player player)
    {
        if (player == null || !player.IsConnected) return;

        // Включаем коллайдеры обратно
        try
        {
            foreach (var collider in player.GameObject.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = true;
            }
            player.GameObject.layer = 0; // Default
        }
        catch { }

        if (player.Role.Is(out FpcRole fpcRole))
        {
            fpcRole.IsInvisible = false;
        }

        player.IsGodModeEnabled = false;
        player.IsNoclipEnabled = false;
        player.IsMuted = false;
    }

    public static void Register()
    {
        Exiled.Events.Handlers.Player.Hurting += OnHurting;
        Exiled.Events.Handlers.Player.Shooting += OnShooting;
        Exiled.Events.Handlers.Player.DroppingItem += OnDroppingItem;
        Exiled.Events.Handlers.Player.PickingUpItem += OnPickingUpItem;
        Exiled.Events.Handlers.Player.DroppingAmmo += OnDroppingAmmo;
        Exiled.Events.Handlers.Player.InteractingDoor += OnInteractingDoor;
        Exiled.Events.Handlers.Player.InteractingLocker += OnInteractingLocker;
        Exiled.Events.Handlers.Player.InteractingElevator += OnInteractingElevator;
        Exiled.Events.Handlers.Player.InteractingEmergencyButton += OnInteractingEmergencyButton;
        Exiled.Events.Handlers.Player.ActivatingWorkstation += OnActivatingWorkstation;
        Exiled.Events.Handlers.Player.DeactivatingWorkstation += OnDeactivatingWorkstation;
        Exiled.Events.Handlers.Player.ActivatingWarheadPanel += OnActivatingWarheadPanel;
        Exiled.Events.Handlers.Player.InteractingShootingTarget += OnInteractingShootingTarget;
        Exiled.Events.Handlers.Player.EnteringPocketDimension += OnEnteringPocketDimension;
        Exiled.Events.Handlers.Player.TriggeringTesla += OnTriggeringTesla;
        Exiled.Events.Handlers.Player.OpeningGenerator += OnOpeningGenerator;
        Exiled.Events.Handlers.Player.UnlockingGenerator += OnUnlockingGenerator;
        Exiled.Events.Handlers.Player.ActivatingGenerator += OnActivatingGenerator;
        Exiled.Events.Handlers.Player.StoppingGenerator += OnStoppingGenerator;

        Exiled.Events.Handlers.Scp049.ActivatingSense += OnActivatingSense;
        Exiled.Events.Handlers.Scp049.StartingRecall += OnStartingRecall;
        Exiled.Events.Handlers.Scp173.AddingObserver += OnAddingObserver;
        Exiled.Events.Handlers.Scp096.AddingTarget += OnAddingTarget;
        Exiled.Events.Handlers.Scp3114.Strangling += OnStrangling;
        Exiled.Events.Handlers.Scp3114.Disguising += OnDisguising;
        Exiled.Events.Handlers.Scp330.InteractingScp330 += OnInteractingScp330;

        Exiled.Events.Handlers.Warhead.ChangingLeverStatus += OnChangingLeverStatus;
        Exiled.Events.Handlers.Warhead.Starting += OnStartingWarhead;
        Exiled.Events.Handlers.Warhead.Stopping += OnStoppingWarhead;

        Exiled.Events.Handlers.Scp914.Activating += OnActivatingScp914;
        Exiled.Events.Handlers.Scp914.ChangingKnobSetting += OnChangingKnobSettingScp914;
    }

    public static void Unregister()
    {
        Exiled.Events.Handlers.Player.Hurting -= OnHurting;
        Exiled.Events.Handlers.Player.Shooting -= OnShooting;
        Exiled.Events.Handlers.Player.DroppingItem -= OnDroppingItem;
        Exiled.Events.Handlers.Player.PickingUpItem -= OnPickingUpItem;
        Exiled.Events.Handlers.Player.DroppingAmmo -= OnDroppingAmmo;
        Exiled.Events.Handlers.Player.InteractingDoor -= OnInteractingDoor;
        Exiled.Events.Handlers.Player.InteractingLocker -= OnInteractingLocker;
        Exiled.Events.Handlers.Player.InteractingElevator -= OnInteractingElevator;
        Exiled.Events.Handlers.Player.InteractingEmergencyButton -= OnInteractingEmergencyButton;
        Exiled.Events.Handlers.Player.ActivatingWorkstation -= OnActivatingWorkstation;
        Exiled.Events.Handlers.Player.DeactivatingWorkstation -= OnDeactivatingWorkstation;
        Exiled.Events.Handlers.Player.ActivatingWarheadPanel -= OnActivatingWarheadPanel;
        Exiled.Events.Handlers.Player.InteractingShootingTarget -= OnInteractingShootingTarget;
        Exiled.Events.Handlers.Player.EnteringPocketDimension -= OnEnteringPocketDimension;
        Exiled.Events.Handlers.Player.TriggeringTesla -= OnTriggeringTesla;
        Exiled.Events.Handlers.Player.OpeningGenerator -= OnOpeningGenerator;
        Exiled.Events.Handlers.Player.UnlockingGenerator -= OnUnlockingGenerator;
        Exiled.Events.Handlers.Player.ActivatingGenerator -= OnActivatingGenerator;
        Exiled.Events.Handlers.Player.StoppingGenerator -= OnStoppingGenerator;

        Exiled.Events.Handlers.Scp049.ActivatingSense -= OnActivatingSense;
        Exiled.Events.Handlers.Scp049.StartingRecall -= OnStartingRecall;
        Exiled.Events.Handlers.Scp173.AddingObserver -= OnAddingObserver;
        Exiled.Events.Handlers.Scp096.AddingTarget -= OnAddingTarget;
        Exiled.Events.Handlers.Scp3114.Strangling -= OnStrangling;
        Exiled.Events.Handlers.Scp3114.Disguising -= OnDisguising;
        Exiled.Events.Handlers.Scp330.InteractingScp330 -= OnInteractingScp330;

        Exiled.Events.Handlers.Warhead.ChangingLeverStatus -= OnChangingLeverStatus;
        Exiled.Events.Handlers.Warhead.Starting -= OnStartingWarhead;
        Exiled.Events.Handlers.Warhead.Stopping -= OnStoppingWarhead;

        Exiled.Events.Handlers.Scp914.Activating -= OnActivatingScp914;
        Exiled.Events.Handlers.Scp914.ChangingKnobSetting -= OnChangingKnobSettingScp914;
    }

    private static bool IsVanished(Player? player) => VanishFeature.IsVanished(player);

    private static void OnHurting(HurtingEventArgs ev)
    {
        if (IsVanished(ev.Player) || IsVanished(ev.Attacker))
        {
            ev.IsAllowed = false;
            ev.Amount = 0f;
        }
    }

    private static void OnShooting(ShootingEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnDroppingItem(DroppingItemEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnPickingUpItem(PickingUpItemEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnDroppingAmmo(DroppingAmmoEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnInteractingDoor(InteractingDoorEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnInteractingLocker(InteractingLockerEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnInteractingElevator(InteractingElevatorEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnInteractingEmergencyButton(InteractingEmergencyButtonEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnActivatingWorkstation(ActivatingWorkstationEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnDeactivatingWorkstation(DeactivatingWorkstationEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnActivatingWarheadPanel(ActivatingWarheadPanelEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnInteractingShootingTarget(InteractingShootingTargetEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnOpeningGenerator(OpeningGeneratorEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnUnlockingGenerator(UnlockingGeneratorEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnActivatingGenerator(ActivatingGeneratorEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnStoppingGenerator(StoppingGeneratorEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnEnteringPocketDimension(EnteringPocketDimensionEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnActivatingSense(ActivatingSenseEventArgs ev) { if (IsVanished(ev.Target)) ev.IsAllowed = false; }
    private static void OnStartingRecall(StartingRecallEventArgs ev) { if (IsVanished(ev.Target)) ev.IsAllowed = false; }
    private static void OnAddingObserver(AddingObserverEventArgs ev) { if (IsVanished(ev.Observer)) ev.IsAllowed = false; }
    private static void OnAddingTarget(AddingTargetEventArgs ev) { if (IsVanished(ev.Target)) ev.IsAllowed = false; }
    private static void OnStrangling(StranglingEventArgs ev) { if (IsVanished(ev.Target)) ev.IsAllowed = false; }
    private static void OnDisguising(DisguisingEventArgs ev) { if (ev.Ragdoll?.Owner != null && IsVanished(ev.Ragdoll.Owner)) ev.IsAllowed = false; }
    private static void OnInteractingScp330(InteractingScp330EventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnChangingLeverStatus(ChangingLeverStatusEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnStartingWarhead(StartingEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnStoppingWarhead(StoppingEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnActivatingScp914(ActivatingEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }
    private static void OnChangingKnobSettingScp914(ChangingKnobSettingEventArgs ev) { if (IsVanished(ev.Player)) ev.IsAllowed = false; }

    private static void OnTriggeringTesla(TriggeringTeslaEventArgs ev)
    {
        if (IsVanished(ev.Player))
        {
            ev.IsAllowed = false;
            ev.IsTriggerable = false;
            ev.IsInIdleRange = false;
            ev.IsInHurtingRange = false;
        }
    }
}
