using System;
using Capy.NoRules.Config;
using Capy.NoRules.EventHandlers;
using Capy.NoRules.Features;
using Exiled.API.Features;
using PlayerEventsHandler = Exiled.Events.Handlers.Player;
using ServerEventsHandler = Exiled.Events.Handlers.Server;

namespace Capy.NoRules;

/// <summary>
/// Главный плагин игрового режима NoRules серверов Капибара SCP:SL.
/// </summary>
public sealed class NoRulesPlugin : Plugin<NoRulesConfig>
{
    public override string Name => "CapyLib.NoRules";
    public override string Author => "CapybaraPR";
    public override string Prefix => "norules";
    public override Version Version => new(1, 0, 0);
    public override Version RequiredExiledVersion => new(8, 9, 0);

    public static NoRulesPlugin Instance { get; private set; } = null!;

    public AutoDoorsFeature AutoDoors { get; private set; } = null!;
    public HitmarkerFeature Hitmarkers { get; private set; } = null!;
    public FastDisarmFeature FastDisarm { get; private set; } = null!;
    public BroadcastsFeature Broadcasts { get; private set; } = null!;

    private PlayerEvents _playerEvents = null!;
    private ServerEvents _serverEvents = null!;
    private bool _isEventsRegistered;

    public override void OnEnabled()
    {
        Instance = this;

        // Проверка привязки к конкретным портам сервера (например, 7777)
        if (Config.TargetPorts != null && Config.TargetPorts.Count > 0 && !Config.TargetPorts.Contains(Server.Port))
        {
            Log.Info($"[NoRules] Сервер запущен на порту {Server.Port}, плагин NoRules настроен только для портов: [{string.Join(", ", Config.TargetPorts)}]. Плагин деактивирован.");
            return;
        }

        RegisterFeatures();
        RegisterEvents();

        Log.Info($"[NoRules] Плагин успешно включен (v{Version}) на порту {Server.Port}.");
        base.OnEnabled();
    }

    public override void OnDisabled()
    {
        UnregisterEvents();

        Instance = null!;
        Log.Info("[NoRules] Плагин выключен.");
        base.OnDisabled();
    }

    private void RegisterFeatures()
    {
        AutoDoors = new AutoDoorsFeature(Config.AutoDoors);
        Hitmarkers = new HitmarkerFeature(Config.Hitmarkers);
        FastDisarm = new FastDisarmFeature(Config.FastDisarm);
        Broadcasts = new BroadcastsFeature(Config.Broadcasts);

        _playerEvents = new PlayerEvents(AutoDoors, Hitmarkers, FastDisarm, Broadcasts);
        _serverEvents = new ServerEvents(Broadcasts);
    }

    private void RegisterEvents()
    {
        if (_isEventsRegistered) return;

        PlayerEventsHandler.InteractingDoor += _playerEvents.OnInteractingDoor;
        PlayerEventsHandler.Hurting += _playerEvents.OnHurting;
        PlayerEventsHandler.Handcuffing += _playerEvents.OnHandcuffing;
        PlayerEventsHandler.Verified += _playerEvents.OnVerified;

        ServerEventsHandler.RoundStarted += _serverEvents.OnRoundStarted;
        ServerEventsHandler.RoundEnded += _serverEvents.OnRoundEnded;

        _isEventsRegistered = true;
    }

    private void UnregisterEvents()
    {
        if (!_isEventsRegistered) return;

        if (_playerEvents != null)
        {
            PlayerEventsHandler.InteractingDoor -= _playerEvents.OnInteractingDoor;
            PlayerEventsHandler.Hurting -= _playerEvents.OnHurting;
            PlayerEventsHandler.Handcuffing -= _playerEvents.OnHandcuffing;
            PlayerEventsHandler.Verified -= _playerEvents.OnVerified;
        }

        if (_serverEvents != null)
        {
            ServerEventsHandler.RoundStarted -= _serverEvents.OnRoundStarted;
            ServerEventsHandler.RoundEnded -= _serverEvents.OnRoundEnded;
        }

        _isEventsRegistered = false;
    }
}
