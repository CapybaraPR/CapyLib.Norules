using System;
using Capy.NoRules.Config;
using Capy.NoRules.EventHandlers;
using Capy.NoRules.Features;
using Exiled.API.Features;
using PlayerEventsHandler = Exiled.Events.Handlers.Player;
using Scp330EventsHandler = Exiled.Events.Handlers.Scp330;
using ServerEventsHandler = Exiled.Events.Handlers.Server;

namespace Capy.NoRules;

/// <summary>
/// Главный плагин игрового режима NoRules серверов Капибара SCP:SL.
/// Включает 8 ключевых механик: .kill/.res, FriendlyFire в конце раунда, Хитмаркеры,
/// Бесконечные ресурсы (InfinityStuff), Мониторинг Интеркома, Розовую конфету, Магическую монетку и Расширенный побег.
/// </summary>
public sealed class NoRulesPlugin : Plugin<NoRulesConfig>
{
    public override string Name => "CapyLib.NoRules";
    public override string Author => "CapybaraPR";
    public override string Prefix => "norules";
    public override Version Version => new(1, 0, 0);
    public override Version RequiredExiledVersion => new(8, 9, 0);

    public static NoRulesPlugin Instance { get; private set; } = null!;

    // 8 Игровых подсистем
    public DotResKillFeature DotResKill { get; private set; } = null!;
    public FriendlyFireFeature FriendlyFire { get; private set; } = null!;
    public HitmarkerFeature Hitmarkers { get; private set; } = null!;
    public InfinityStuffFeature InfinityStuff { get; private set; } = null!;
    public IntercomListFeature IntercomList { get; private set; } = null!;
    public PinkCandyFeature PinkCandy { get; private set; } = null!;
    public BetterCoinsFeature BetterCoins { get; private set; } = null!;
    public BetterEscapeFeature BetterEscape { get; private set; } = null!;

    private PlayerEvents _playerEvents = null!;
    private ServerEvents _serverEvents = null!;
    private bool _isEventsRegistered;

    public override void OnEnabled()
    {
        Instance = this;

        RegisterFeatures();
        RegisterEvents();

        Capy.Commands.HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .res</color>                   <color=#c2c2c2>-- Быстрое возрождение в первые 3 мин (наблюдатели)</color>");
        Capy.Commands.HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .kill</color>                  <color=#c2c2c2>-- Совершить самоубийство (живые игроки)</color>");

        Log.Info($"[NoRules] Плагин успешно запущен (v{Version}) со всеми 8 игровыми модулями.");
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
        DotResKill = new DotResKillFeature(Config.DotResKill);
        FriendlyFire = new FriendlyFireFeature(Config.FriendlyFire);
        Hitmarkers = new HitmarkerFeature(Config.Hitmarkers);
        InfinityStuff = new InfinityStuffFeature(Config.InfinityStuff);
        IntercomList = new IntercomListFeature(Config.IntercomList);
        PinkCandy = new PinkCandyFeature(Config.PinkCandy);
        BetterCoins = new BetterCoinsFeature(Config.BetterCoins);
        BetterEscape = new BetterEscapeFeature(Config.BetterEscape);

        _playerEvents = new PlayerEvents(Hitmarkers, DotResKill, BetterCoins, BetterEscape, InfinityStuff);
        _serverEvents = new ServerEvents(DotResKill, FriendlyFire, IntercomList);
    }

    private void RegisterEvents()
    {
        if (_isEventsRegistered) return;

        PlayerEventsHandler.Hurting += _playerEvents.OnHurting;
        PlayerEventsHandler.Died += _playerEvents.OnDied;
        PlayerEventsHandler.Spawned += _playerEvents.OnSpawned;
        PlayerEventsHandler.FlippingCoin += _playerEvents.OnFlippingCoin;
        PlayerEventsHandler.Escaping += _playerEvents.OnEscaping;
        PlayerEventsHandler.UsingRadioBattery += _playerEvents.OnUsingRadioBattery;
        PlayerEventsHandler.ReloadingWeapon += _playerEvents.OnReloadingWeapon;
        PlayerEventsHandler.DroppingAmmo += _playerEvents.OnDroppingAmmo;
        PlayerEventsHandler.PickingUpItem += _playerEvents.OnPickingUpItem;

        ServerEventsHandler.RoundStarted += _serverEvents.OnRoundStarted;
        ServerEventsHandler.RoundEnded += _serverEvents.OnRoundEnded;
        ServerEventsHandler.WaitingForPlayers += _serverEvents.OnWaitingForPlayers;

        Scp330EventsHandler.InteractingScp330 += PinkCandy.OnInteractingScp330;

        _isEventsRegistered = true;
    }

    private void UnregisterEvents()
    {
        if (!_isEventsRegistered) return;

        if (_playerEvents != null)
        {
            PlayerEventsHandler.Hurting -= _playerEvents.OnHurting;
            PlayerEventsHandler.Died -= _playerEvents.OnDied;
            PlayerEventsHandler.Spawned -= _playerEvents.OnSpawned;
            PlayerEventsHandler.FlippingCoin -= _playerEvents.OnFlippingCoin;
            PlayerEventsHandler.Escaping -= _playerEvents.OnEscaping;
            PlayerEventsHandler.UsingRadioBattery -= _playerEvents.OnUsingRadioBattery;
            PlayerEventsHandler.ReloadingWeapon -= _playerEvents.OnReloadingWeapon;
            PlayerEventsHandler.DroppingAmmo -= _playerEvents.OnDroppingAmmo;
            PlayerEventsHandler.PickingUpItem -= _playerEvents.OnPickingUpItem;
        }

        if (_serverEvents != null)
        {
            ServerEventsHandler.RoundStarted -= _serverEvents.OnRoundStarted;
            ServerEventsHandler.RoundEnded -= _serverEvents.OnRoundEnded;
            ServerEventsHandler.WaitingForPlayers -= _serverEvents.OnWaitingForPlayers;
        }

        if (PinkCandy != null)
        {
            Scp330EventsHandler.InteractingScp330 -= PinkCandy.OnInteractingScp330;
        }

        _isEventsRegistered = false;
    }
}
