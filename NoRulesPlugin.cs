using System;

using Capy.Commands;

using Capy.NoRules.Config;

using Capy.NoRules.EventHandlers;

using Capy.NoRules.Features;

using Exiled.API.Features;

using PlayerEventsHandler = Exiled.Events.Handlers.Player;

using Scp096EventsHandler = Exiled.Events.Handlers.Scp096;

using Scp173EventsHandler = Exiled.Events.Handlers.Scp173;

using Scp330EventsHandler = Exiled.Events.Handlers.Scp330;

using ServerEventsHandler = Exiled.Events.Handlers.Server;



namespace Capy.NoRules;



/// <summary>

/// Главный плагин игрового режима NoRules серверов Капибара SCP:SL.

/// Включает: .kill/.res, FriendlyFire в конце раунда, Хитмаркеры, Бесконечные ресурсы,

/// Мониторинг Интеркома, Розовую конфету, Магическую монетку, Расширенный побег, gci, gcr и Vanish.

/// </summary>

public sealed class NoRulesPlugin : Plugin<NoRulesConfig>

{

    public override string Name => "CapyLib.NoRules";

    public override string Author => "CapybaraPR";

    public override string Prefix => "norules";

    public override Version Version => new(1, 3, 1);

    public override Version RequiredExiledVersion => new(9, 0, 0);



    public static NoRulesPlugin Instance { get; private set; } = null!;



    // Игровые подсистемы

    public DotResKillFeature DotResKill { get; private set; } = null!;

    public FriendlyFireFeature FriendlyFire { get; private set; } = null!;

    public HitmarkerFeature Hitmarkers { get; private set; } = null!;

    public InfinityStuffFeature InfinityStuff { get; private set; } = null!;

    public IntercomListFeature IntercomList { get; private set; } = null!;

    public PinkCandyFeature PinkCandy { get; private set; } = null!;

    public BetterCoinsFeature BetterCoins { get; private set; } = null!;

    public BetterEscapeFeature BetterEscape { get; private set; } = null!;

    public VanishFeature Vanish { get; private set; } = null!;

    public CapybaraPetFeature CapybaraPet { get; private set; } = null!;

    public Scp120Feature Scp120 { get; private set; } = null!;

    public Scp294Feature Scp294 { get; private set; } = null!;

    public FacilityAuthFeature FacilityAuth { get; private set; } = null!;

    public ShowReportsFeature ShowReports { get; private set; } = null!;

    public PlayerXpFeature PlayerXp { get; private set; } = null!;
    public Scp1162Feature Scp1162 { get; private set; } = null!;
    public ChatSayFeature ChatSay { get; private set; } = null!;
    public CallVoteFeature CallVote { get; private set; } = null!;
    public ScpSwapFeature ScpSwap { get; private set; } = null!;
    public RemoteKeycardFeature RemoteKeycard { get; private set; } = null!;

    private PlayerEvents _playerEvents = null!;

    private ServerEvents _serverEvents = null!;

    private bool _isEventsRegistered;



    public override void OnEnabled()

    {

        Instance = this;



        RegisterFeatures();

        RegisterEvents();



        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .res</color>                   <color=#c2c2c2>-- Быстрое возрождение в первые 3 мин (наблюдатели)</color>");

        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .kill</color>                  <color=#c2c2c2>-- Совершить самоубийство (живые игроки)</color>");

        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .vanish (.v, .spec)</color>   <color=#c2c2c2>-- Режим свободного наблюдателя (из спектаторов)</color>");

        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* .drink (.dr)</color>       <color=#c2c2c2>-- Выбрать напиток SCP-294 (кофемашина в офисах)</color>");

        HelpMessageBuilder.RegisterCustomCommand("<color=#ffd285>* /level, /top</color>        <color=#c2c2c2>-- Уровень и топ игроков в нашем Discord</color>");



        Log.Info($"[NoRules] Плагин успешно запущен (v{Version}) со всеми игровыми модулями, Vanish, CapybaraPet и SCP-120.");

        base.OnEnabled();

    }



    public override void OnDisabled()

    {

        UnregisterEvents();

        Scp120?.Dispose();

        Scp294?.Disable();

        FacilityAuth?.Disable();

        ShowReports?.Disable();

        PlayerXp?.Disable();



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

        Vanish = new VanishFeature();

        Vanish.Enable();

        CapybaraPet = new CapybaraPetFeature(Config.CapybaraPet);

        Scp120 = new Scp120Feature(Config.Scp120);

        Scp294 = new Scp294Feature(Config.Scp294);

        Scp294.Enable();



        FacilityAuth = new FacilityAuthFeature(Config.FacilityAuth);

        FacilityAuth.Enable();



        ShowReports = new ShowReportsFeature(Config.ShowReports);

        ShowReports.Enable();



        PlayerXp = new PlayerXpFeature(Config.PlayerXp);

        PlayerXp.Enable();

        Scp1162 = new Scp1162Feature(Config.Scp1162);
        Scp1162.Enable();

        ChatSay = new ChatSayFeature(Config.ChatSay);

        CallVote = new CallVoteFeature(Config.CallVote);

        ScpSwap = new ScpSwapFeature(Config.ScpSwap);

        RemoteKeycard = new RemoteKeycardFeature(Config.RemoteKeycard);
        RemoteKeycard.Enable();



        _playerEvents = new PlayerEvents(Hitmarkers, DotResKill, BetterCoins, BetterEscape, InfinityStuff, Vanish, CapybaraPet, Scp120);

        _serverEvents = new ServerEvents(DotResKill, FriendlyFire, IntercomList, Scp120);

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

        PlayerEventsHandler.SearchingPickup += _playerEvents.OnSearchingPickup;

        PlayerEventsHandler.Handcuffing += _playerEvents.OnHandcuffing;

        PlayerEventsHandler.Left += _playerEvents.OnLeft;



        Exiled.Events.Handlers.Map.PickupAdded += InfinityStuff.OnPickupAdded;



        ServerEventsHandler.RoundStarted += _serverEvents.OnRoundStarted;

        ServerEventsHandler.RoundStarted += InfinityStuff.OnRoundStarted;

        ServerEventsHandler.RoundEnded += _serverEvents.OnRoundEnded;

        ServerEventsHandler.WaitingForPlayers += _serverEvents.OnWaitingForPlayers;

        ServerEventsHandler.RoundStarted += Scp1162.OnRoundStarted;
        ServerEventsHandler.WaitingForPlayers += Scp1162.OnWaitingForPlayers;
        ServerEventsHandler.RestartingRound += ChatSay.OnRestartingRound;
        ServerEventsHandler.RoundStarted += ScpSwap.OnRoundStarted;
        ServerEventsHandler.WaitingForPlayers += ScpSwap.OnWaitingForPlayers;
        ServerEventsHandler.RestartingRound += ScpSwap.OnRestartingRound;
        ServerEventsHandler.RestartingRound += CallVote.OnRestartingRound;
        ServerEventsHandler.RestartingRound += PinkCandyStaticRestart;
        Exiled.Events.Handlers.Player.Left += PinkCandyStaticLeft;

        ServerEventsHandler.RespawningTeam += Vanish.OnRespawningTeam;

        ServerEventsHandler.RestartingRound += Vanish.OnRoundRestarted;

        ServerEventsHandler.RestartingRound += CapybaraPet.OnRoundRestarted;



        Scp330EventsHandler.InteractingScp330 += PinkCandy.OnInteractingScp330;



        _isEventsRegistered = true;

    }



    private void UnregisterEvents()

    {

        if (!_isEventsRegistered) return;



        if (_playerEvents != null)

        {

            ServerEventsHandler.RoundStarted -= Scp1162.OnRoundStarted;
            ServerEventsHandler.WaitingForPlayers -= Scp1162.OnWaitingForPlayers;
            ServerEventsHandler.RestartingRound -= ChatSay.OnRestartingRound;
            ServerEventsHandler.RoundStarted -= ScpSwap.OnRoundStarted;
            ServerEventsHandler.WaitingForPlayers -= ScpSwap.OnWaitingForPlayers;
            ServerEventsHandler.RestartingRound -= ScpSwap.OnRestartingRound;
            ServerEventsHandler.RestartingRound -= CallVote.OnRestartingRound;
            ServerEventsHandler.RestartingRound -= PinkCandyStaticRestart;
            Exiled.Events.Handlers.Player.Left -= PinkCandyStaticLeft;

            PlayerEventsHandler.Hurting -= _playerEvents.OnHurting;

            PlayerEventsHandler.Died -= _playerEvents.OnDied;

            PlayerEventsHandler.Spawned -= _playerEvents.OnSpawned;

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



        Exiled.Events.Handlers.Map.PickupAdded -= InfinityStuff.OnPickupAdded;



        if (_serverEvents != null)

        {

            ServerEventsHandler.RoundStarted -= _serverEvents.OnRoundStarted;

            ServerEventsHandler.RoundEnded -= _serverEvents.OnRoundEnded;

            ServerEventsHandler.WaitingForPlayers -= _serverEvents.OnWaitingForPlayers;

        }



        if (InfinityStuff != null)

        {

            ServerEventsHandler.RoundStarted -= InfinityStuff.OnRoundStarted;

        }



        if (Vanish != null)

        {

            ServerEventsHandler.RespawningTeam -= Vanish.OnRespawningTeam;

            ServerEventsHandler.RestartingRound -= Vanish.OnRoundRestarted;

            Vanish.Disable();

        }



        if (CapybaraPet != null)

        {

            ServerEventsHandler.RestartingRound -= CapybaraPet.OnRoundRestarted;

            CapybaraPet.OnRoundRestarted();

        }



        if (PinkCandy != null)

        {

            Scp330EventsHandler.InteractingScp330 -= PinkCandy.OnInteractingScp330;

        }



        _isEventsRegistered = false;
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

