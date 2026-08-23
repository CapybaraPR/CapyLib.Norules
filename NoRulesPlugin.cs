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

    public HitmarkerFeature Hitmarkers { get; private set; } = null!;
    public AnnouncementsFeature Announcements { get; private set; } = null!;

    private PlayerEvents _playerEvents = null!;
    private ServerEvents _serverEvents = null!;
    private bool _isEventsRegistered;

    public override void OnEnabled()
    {
        Instance = this;

        RegisterFeatures();
        RegisterEvents();

        Log.Info($"[NoRules] Плагин успешно включен (v{Version}).");
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
        Hitmarkers = new HitmarkerFeature(Config.Hitmarkers);
        Announcements = new AnnouncementsFeature(Config.Announcements);

        _playerEvents = new PlayerEvents(Hitmarkers, Announcements);
        _serverEvents = new ServerEvents(Announcements);
    }

    private void RegisterEvents()
    {
        if (_isEventsRegistered) return;

        PlayerEventsHandler.Hurting += _playerEvents.OnHurting;
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
            PlayerEventsHandler.Hurting -= _playerEvents.OnHurting;
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
