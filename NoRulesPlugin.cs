using System;
using HarmonyLib;
using Capy.Commands;
using Capy.Core.DRM;
using Capy.NoRules.Addons;
using Capy.NoRules.Concepts;
using Capy.NoRules.Concepts.Co2;
using Capy.NoRules.Concepts.Hackers;
using Capy.NoRules.Concepts.Scp008;
using Capy.NoRules.Config;
using Capy.NoRules.Controllers;
using Capy.NoRules.Core;
using Capy.NoRules.EventHandlers;
using Capy.NoRules.Modules;
using Capy.NoRules.Scps;
using Capy.NoRules.Spawns;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Server;
using PlayerEventsHandler = Exiled.Events.Handlers.Player;
using Scp096EventsHandler = Exiled.Events.Handlers.Scp096;
using Scp173EventsHandler = Exiled.Events.Handlers.Scp173;
using Scp330EventsHandler = Exiled.Events.Handlers.Scp330;
using ServerEventsHandler = Exiled.Events.Handlers.Server;

namespace Capy.NoRules;

/// <summary>
/// Главный плагин игрового режима NoRules серверов Капибара SCP:SL.
/// Автоматически управляет всеми подсистемами, фичами и концептами через FeatureRegistry на базе рефлексии.
/// </summary>
public sealed class NoRulesPlugin : Plugin<NoRulesConfig>
{
    public override string Name => "CapyLib.NoRules";
    public override string Author => "CapybaraPR";
    public override string Prefix => "norules";
    public override Version Version => new(1, 3, 4);
    public override Version RequiredExiledVersion => new(9, 0, 0);

    public static NoRulesPlugin Instance { get; private set; } = null!;

    // Динамический доступ к зарегистрированным фичам через рефлексивный реестр
    public DotResKillFeature DotResKill => FeatureRegistry.Get<DotResKillFeature>();
    public FriendlyFireFeature FriendlyFire => FeatureRegistry.Get<FriendlyFireFeature>();
    public HitmarkerFeature Hitmarkers => FeatureRegistry.Get<HitmarkerFeature>();
    public InfinityStuffFeature InfinityStuff => FeatureRegistry.Get<InfinityStuffFeature>();
    public IntercomListFeature IntercomList => FeatureRegistry.Get<IntercomListFeature>();
    public PinkCandyFeature PinkCandy => FeatureRegistry.Get<PinkCandyFeature>();
    public BetterCoinsFeature BetterCoins => FeatureRegistry.Get<BetterCoinsFeature>();
    public BetterEscapeFeature BetterEscape => FeatureRegistry.Get<BetterEscapeFeature>();
    public VanishFeature Vanish => FeatureRegistry.Get<VanishFeature>();
    public CapybaraPetFeature CapybaraPet => FeatureRegistry.Get<CapybaraPetFeature>();
    public Scp120Feature Scp120 => FeatureRegistry.Get<Scp120Feature>();
    public Scp294Feature Scp294 => FeatureRegistry.Get<Scp294Feature>();
    public FacilityAuthFeature FacilityAuth => FeatureRegistry.Get<FacilityAuthFeature>();
    public ShowReportsFeature ShowReports => FeatureRegistry.Get<ShowReportsFeature>();
    public PlayerXpFeature PlayerXp => FeatureRegistry.Get<PlayerXpFeature>();
    public Scp1162Feature Scp1162 => FeatureRegistry.Get<Scp1162Feature>();
    public ChatSayFeature ChatSay => FeatureRegistry.Get<ChatSayFeature>();
    public CallVoteFeature CallVote => FeatureRegistry.Get<CallVoteFeature>();
    public ScpSwapFeature ScpSwap => FeatureRegistry.Get<ScpSwapFeature>();
    public RemoteKeycardFeature RemoteKeycard => FeatureRegistry.Get<RemoteKeycardFeature>();
    public Co2Concept Co2 => FeatureRegistry.Get<Co2Concept>();
    public HackersConcept Hackers => FeatureRegistry.Get<HackersConcept>();
    public Scp008Concept Scp008 => FeatureRegistry.Get<Scp008Concept>();
    public Old173SpawnFeature Old173Spawn => FeatureRegistry.Get<Old173SpawnFeature>();
    public DonateControllerFeature DonateController => FeatureRegistry.Get<DonateControllerFeature>();
    public LobbyFeature Lobby => FeatureRegistry.Get<LobbyFeature>();
    public LobbyMusicFeature LobbyMusic => FeatureRegistry.Get<LobbyMusicFeature>();

    private PlayerEvents _playerEvents = null!;
    private ServerEvents _serverEvents = null!;
    private bool _isEventsRegistered;
    private bool _isStarted;

    public override void OnEnabled()
    {
        Instance = this;

        LicenseManager.LicenseConfirmed += OnLicenseConfirmed;

        if (LicenseManager.IsLicenseValid)
        {
            StartPlugin();
        }
        else
        {
            Log.Warn("[NoRules] Ожидание подтверждения лицензии CapyLib для активации режима NoRules...");
        }

        base.OnEnabled();
    }

    private void OnLicenseConfirmed()
    {
        if (!_isStarted)
        {
            StartPlugin();
        }
    }

    private void StartPlugin()
    {
        if (_isStarted) return;
        _isStarted = true;

        AlphaController.Init();
        ConceptsController.Init();
        SpawnManager.Init();

        // 1. Автоматическая рефлексивная инициализация всех фичей
        FeatureRegistry.InitializeAll(Config);

        // 2. Инициализация диспетчеров событий
        _playerEvents = new PlayerEvents(Hitmarkers, DotResKill, BetterCoins, BetterEscape, InfinityStuff, Vanish, CapybaraPet, Scp120, Lobby);
        _serverEvents = new ServerEvents(DotResKill, FriendlyFire, IntercomList, Scp120, Lobby, LobbyMusic);

        RegisterEvents();

        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .res</color>                   <color=#c2c2c2>-- Быстрое возрождение в первые 3 мин (наблюдатели)</color>");
        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .kill</color>                  <color=#c2c2c2>-- Совершить самоубийство (живые игроки)</color>");
        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .get [предмет]</color>          <color=#c2c2c2>-- Забрать купленные предметы с сайта</color>");
        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .sync</color>                  <color=#c2c2c2>-- Синхронизировать донат-ранг и бейдж</color>");
        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .vanish (.v, .spec)</color>   <color=#c2c2c2>-- Режим свободного наблюдателя (из спектаторов)</color>");
        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .drink (.dr)</color>       <color=#c2c2c2>-- Выбрать напиток SCP-294 (кофемашина в офисах)</color>");
        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* /level, /top</color>        <color=#c2c2c2>-- Уровень и топ игроков в нашем Discord</color>");

        Log.Info($"[NoRules] Режим успешно запущен v{Version} ({FeatureRegistry.Features.Count} фичей и концептов, 3D-Лобби, кастомный спавнер).");
    }

    public override void OnDisabled()
    {
        LicenseManager.LicenseConfirmed -= OnLicenseConfirmed;
        _isStarted = false;

        UnregisterEvents();

        SpawnManager.Unload();
        AlphaController.Unload();
        ConceptsController.Unload();

        // Рефлексивная выгрузка всех фичей
        FeatureRegistry.ShutdownAll();

        Instance = null!;
        Log.Info("[NoRules] Плагин выключен.");
        base.OnDisabled();
    }

    private void RegisterEvents()
    {
        if (_isEventsRegistered) return;

        // Player events
        PlayerEventsHandler.Hurting += _playerEvents.OnHurting;
        PlayerEventsHandler.Died += _playerEvents.OnDied;
        PlayerEventsHandler.Spawned += _playerEvents.OnSpawned;
        PlayerEventsHandler.Verified += _playerEvents.OnVerified;
        PlayerEventsHandler.FlippingCoin += _playerEvents.OnFlippingCoin;
        PlayerEventsHandler.Escaping += _playerEvents.OnEscaping;
        PlayerEventsHandler.UsingRadioBattery += _playerEvents.OnUsingRadioBattery;
        PlayerEventsHandler.ReloadingWeapon += _playerEvents.OnReloadingWeapon;
        PlayerEventsHandler.DroppingAmmo += _playerEvents.OnDroppingAmmo;
        PlayerEventsHandler.PickingUpItem += _playerEvents.OnPickingUpItem;
        PlayerEventsHandler.SearchingPickup += _playerEvents.OnSearchingPickup;
        PlayerEventsHandler.Handcuffing += _playerEvents.OnHandcuffing;
        PlayerEventsHandler.Left += _playerEvents.OnLeft;

        // Map events
        if (InfinityStuff != null)
            Exiled.Events.Handlers.Map.PickupAdded += InfinityStuff.OnPickupAdded;

        // Server events
        ServerEventsHandler.RoundStarted += _serverEvents.OnRoundStarted;
        if (InfinityStuff != null)
            ServerEventsHandler.RoundStarted += InfinityStuff.OnRoundStarted;
        ServerEventsHandler.RoundEnded += _serverEvents.OnRoundEnded;
        ServerEventsHandler.WaitingForPlayers += _serverEvents.OnWaitingForPlayers;
        ServerEventsHandler.RestartingRound += _serverEvents.OnRestartingRound;

        // Addon events
        if (ChatSay != null)
            ServerEventsHandler.RestartingRound += ChatSay.OnRestartingRound;
        if (ScpSwap != null)
        {
            ServerEventsHandler.RoundStarted += ScpSwap.OnRoundStarted;
            ServerEventsHandler.WaitingForPlayers += ScpSwap.OnWaitingForPlayers;
            ServerEventsHandler.RestartingRound += ScpSwap.OnRestartingRound;
        }
        if (CallVote != null)
            ServerEventsHandler.RestartingRound += CallVote.OnRestartingRound;
        
        ServerEventsHandler.RestartingRound += PinkCandyStaticRestart;
        Exiled.Events.Handlers.Player.Left += PinkCandyStaticLeft;

        // Wave & respawn events
        ServerEventsHandler.RespawningTeam += WaveDispatcher;
        if (Vanish != null)
        {
            ServerEventsHandler.RespawningTeam += Vanish.OnRespawningTeam;
            ServerEventsHandler.RestartingRound += Vanish.OnRoundRestarted;
        }
        if (CapybaraPet != null)
        {
            ServerEventsHandler.RestartingRound += CapybaraPet.OnRoundRestarted;
        }

        // SCP events
        if (PinkCandy != null)
        {
            Scp330EventsHandler.InteractingScp330 += PinkCandy.OnInteractingScp330;
        }

        _isEventsRegistered = true;
    }

    private void UnregisterEvents()
    {
        if (!_isEventsRegistered) return;

        // Player events
        if (_playerEvents != null)
        {
            PlayerEventsHandler.Hurting -= _playerEvents.OnHurting;
            PlayerEventsHandler.Died -= _playerEvents.OnDied;
            PlayerEventsHandler.Spawned -= _playerEvents.OnSpawned;
            PlayerEventsHandler.Verified -= _playerEvents.OnVerified;
            PlayerEventsHandler.FlippingCoin -= _playerEvents.OnFlippingCoin;
            PlayerEventsHandler.Escaping -= _playerEvents.OnEscaping;
            PlayerEventsHandler.UsingRadioBattery -= _playerEvents.OnUsingRadioBattery;
            PlayerEventsHandler.ReloadingWeapon -= _playerEvents.OnReloadingWeapon;
            PlayerEventsHandler.DroppingAmmo -= _playerEvents.OnDroppingAmmo;
            PlayerEventsHandler.PickingUpItem -= _playerEvents.OnPickingUpItem;
            PlayerEventsHandler.SearchingPickup -= _playerEvents.OnSearchingPickup;
            PlayerEventsHandler.Handcuffing -= _playerEvents.OnHandcuffing;
            PlayerEventsHandler.Left -= _playerEvents.OnLeft;
        }

        // Map events
        if (InfinityStuff != null)
            Exiled.Events.Handlers.Map.PickupAdded -= InfinityStuff.OnPickupAdded;

        // Server events
        if (_serverEvents != null)
        {
            ServerEventsHandler.RoundStarted -= _serverEvents.OnRoundStarted;
            ServerEventsHandler.RoundEnded -= _serverEvents.OnRoundEnded;
            ServerEventsHandler.WaitingForPlayers -= _serverEvents.OnWaitingForPlayers;
            ServerEventsHandler.RestartingRound -= _serverEvents.OnRestartingRound;
        }

        if (InfinityStuff != null)
        {
            ServerEventsHandler.RoundStarted -= InfinityStuff.OnRoundStarted;
        }

        Lobby?.DespawnLobby();

        // Addon events
        if (ChatSay != null)
            ServerEventsHandler.RestartingRound -= ChatSay.OnRestartingRound;
        if (ScpSwap != null)
        {
            ServerEventsHandler.RoundStarted -= ScpSwap.OnRoundStarted;
            ServerEventsHandler.WaitingForPlayers -= ScpSwap.OnWaitingForPlayers;
            ServerEventsHandler.RestartingRound -= ScpSwap.OnRestartingRound;
        }
        if (CallVote != null)
            ServerEventsHandler.RestartingRound -= CallVote.OnRestartingRound;

        ServerEventsHandler.RestartingRound -= PinkCandyStaticRestart;
        Exiled.Events.Handlers.Player.Left -= PinkCandyStaticLeft;

        // Wave & respawn events
        ServerEventsHandler.RespawningTeam -= WaveDispatcher;

        if (Vanish != null)
        {
            ServerEventsHandler.RespawningTeam -= Vanish.OnRespawningTeam;
            ServerEventsHandler.RestartingRound -= Vanish.OnRoundRestarted;
        }

        if (CapybaraPet != null)
        {
            ServerEventsHandler.RestartingRound -= CapybaraPet.OnRoundRestarted;
        }

        // SCP events
        if (PinkCandy != null)
        {
            Scp330EventsHandler.InteractingScp330 -= PinkCandy.OnInteractingScp330;
        }

        _isEventsRegistered = false;
    }

    private void WaveDispatcher(RespawningTeamEventArgs ev)
    {
        // Если OMEGA боеголовка уже сдетонировала или активен концепт — любые волны блокируются
        if (Hackers?.IsOmegaDetonated == true || ConceptsController.IsActivated)
        {
            ev.IsAllowed = false;
            return;
        }

        // Спавном управляет кастомный SpawnManager (волна 1: Разведгруппа, волна 2: Аварийный отряд, Хаос + Хакеры)
        ev.IsAllowed = false;
    }

    private void PinkCandyStaticRestart()
    {
        PinkCandy?.OnRestartingRound();
    }

    private void PinkCandyStaticLeft(Exiled.Events.EventArgs.Player.LeftEventArgs ev)
    {
        PinkCandy?.OnLeft(ev.Player);
    }
}
