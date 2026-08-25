using System.Collections.Generic;
using System.ComponentModel;
using Exiled.API.Interfaces;
using PlayerRoles;

namespace Capy.NoRules.Config;

/// <summary>
/// Главная конфигурация серверного плагина CapyLib.NoRules со всеми 8 игровыми модулями.
/// </summary>
public sealed class NoRulesConfig : IConfig
{
    [Description("Включен ли плагин NoRules.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Режим подробного логирования для отладки.")]
    public bool Debug { get; set; } = false;

    [Description("1. Настройки системы самоубийства (.kill) и быстрого возрождения (.res).")]
    public DotResKillConfig DotResKill { get; set; } = new();

    [Description("2. Настройки режима Friendly Fire (резня в конце раунда).")]
    public NoRulesFriendlyFireConfig FriendlyFire { get; set; } = new();

    [Description("3. Настройки хитмаркеров и отображения урона.")]
    public HitmarkerConfig Hitmarkers { get; set; } = new();

    [Description("4. Настройки бесконечных ресурсов (патроны, бесконечная рация, очистка дропа патронов).")]
    public InfinityStuffConfig InfinityStuff { get; set; } = new();

    [Description("5. Настройки интерактивного экрана Интеркома (список выживших по фракциям).")]
    public IntercomListConfig IntercomList { get; set; } = new();

    [Description("6. Настройки спавна взрывной Розовой Конфеты (Pink Candy) в SCP-330.")]
    public PinkCandyConfig PinkCandy { get; set; } = new();

    [Description("7. Настройки магической монетки (BetterCoins — телепортация и цитаты).")]
    public BetterCoinsConfig BetterCoins { get; set; } = new();

    [Description("8. Настройки расширенных сценариев побега (BetterEscape).")]
    public BetterEscapeConfig BetterEscape { get; set; } = new();

    [Description("9. Настройки летающего питомца-капибары (Capybara Companion) для выбранных SteamID.")]
    public CapybaraPetConfig CapybaraPet { get; set; } = new();

    [Description("10. Настройки аномального объекта SCP-120 (Детский бассейн-телепорт и трансформация предметов).")]
    public Scp120Config Scp120 { get; set; } = new();

    [Description("11. Настройки SCP-294 (Кофемашина — выдаёт напитки по выбору игрока).")]
    public Scp294Config Scp294 { get; set; } = new();

    [Description("12. Настройки Facility Auth (тесла не бьёт игроков с картой доступа, кроме карты Хаоса).")]
    public FacilityAuthConfig FacilityAuth { get; set; } = new();

    [Description("13. Настройки уведомлений о репортах для администрации.")]
    public ShowReportsConfig ShowReports { get; set; } = new();

    [Description("14. Настройки системы опыта и уровней игроков (синхронизируется с Discord).")]
    public PlayerXpConfig PlayerXp { get; set; } = new();

    [Description("15. SCP-1162 (дыра в камере 173 — суешь предмет, получаешь случайный).")]
    public Scp1162Config Scp1162 { get; set; } = new();

    [Description("16. Глобальный чат через .say (сообщения выводятся всем в HUD).")]
    public ChatSayConfig ChatSay { get; set; } = new();

    [Description("17. Голосование за рестарт раунда (.vote).")]
    public CallVoteConfig CallVote { get; set; } = new();

    [Description("18. Смена SCP-роли командой .swap в начале раунда.")]
    public ScpSwapConfig ScpSwap { get; set; } = new();

    [Description("19. Remote Keycard — карты доступа работают из любого слота инвентаря.")]
    public RemoteKeycardConfig RemoteKeycard { get; set; } = new();

    [Description("20. Концепт «Отряд СО₂» — фракция МОГ с миссией затопления комплекса угарным газом.")]
    public Co2Config Co2 { get; set; } = new();

    [Description("21. Концепт «Хакеры» — группировка с миссией взлома Omega Warhead.")]
    public HackersConfig Hackers { get; set; } = new();

    [Description("22. Концепт «SCP-008» — вирусные трубки, открываемые SCP.")]
    public Scp008Config Scp008 { get; set; } = new();

    [Description("24. Концепт «AirDrop» — грузовой самолёт с лутом над Поверхностью.")]
    public AirDropConfig AirDrop { get; set; } = new();
}

public sealed class Co2Config
{
    [Description("Включен ли концепт «Отряд СО₂».")]
    public bool IsEnabled { get; set; } = true;

    [Description("Шанс замены волны МОГ на отряд СО₂ (в процентах), если условия выполнены.")]
    public int ChancePercent { get; set; } = 50;

    [Description("Минимум спектаторов для формирования отряда.")]
    public int MinSpectators { get; set; } = 4;

    [Description("Максимальный размер отряда.")]
    public int SquadSizeMax { get; set; } = 10;

    [Description("Требовать сбежавших учёных для спавна отряда.")]
    public bool RequireEscapedScientists { get; set; } = true;

    [Description("Радиус взаимодействия с панелями (метры). Нажмите [E] рядом. Панели находятся в комнате турели (H.I.D.) в HCZ.")]
    public float PanelRadius { get; set; } = 2.5f;

    [Description("Панель 1: смещение X относительно центра комнаты турели.")]
    public float Panel1X { get; set; } = 5.29f;

    [Description("Панель 1: смещение Y.")]
    public float Panel1Y { get; set; } = 5.46f;

    [Description("Панель 1: смещение Z.")]
    public float Panel1Z { get; set; } = -2.42f;

    [Description("Панель 1: поворот Y.")]
    public float Panel1RotY { get; set; } = 102.75f;

    [Description("Панель 2: смещение X относительно центра комнаты турели.")]
    public float Panel2X { get; set; } = -6.4f;

    [Description("Панель 2: смещение Y.")]
    public float Panel2Y { get; set; } = 5.45f;

    [Description("Панель 2: смещение Z.")]
    public float Panel2Z { get; set; } = 4.89f;

    [Description("Панель 2: поворот Y.")]
    public float Panel2RotY { get; set; } = 270.25f;

    [Description("Сколько секунд нужно удерживать активацию панели.")]
    public float ActivateConfirmSeconds { get; set; } = 2f;

    [Description("Задержка перед началом отравления после активации (секунды).")]
    public float PoisonDelaySeconds { get; set; } = 160f;

    [Description("Пауза между объявлением герметизации и началом урона (секунды).")]
    public float PoisonGraceSeconds { get; set; } = 36f;

    [Description("Урон от отравления CO2 в секунду (не-SCP игрокам).")]
    public float PoisonDamagePerSecond { get; set; } = 10f;

    [Description("Сколько секунд после начала отравления до конца раунда.")]
    public float EndAfterPoisonSeconds { get; set; } = 60f;

    [Description("Опыт за успешную активацию (без делителя).")]
    public float MissionXp { get; set; } = 500f;

    [Description("CASSIE-сообщение при активации.")]
    public string CassieActivate { get; set; } = "DANGER . DANGER . FACILITY GAS LEAK DETECTED . ALL PERSONNEL EVACUATE IMMEDIATELY";

    [Description("CASSIE-сообщение при отмене.")]
    public string CassieCancel { get; set; } = "GAS LEAK CONTAINED . FACILITY SECURE";
}

public sealed class Scp1162Config
{
    [Description("Включен ли SCP-1162.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Имя схематики дыры.")]
    public string SchematicName { get; set; } = "SCP1162";

    [Description("Смещение дыры относительно центра комнаты по X.")]
    public float OffsetX { get; set; } = 22.1f;

    [Description("Смещение позиции дыры относительно центра комнаты по Y.")]
    public float OffsetY { get; set; } = 13.06f;

    [Description("Смещение позиции дыры относительно центра комнаты по Z.")]
    public float OffsetZ { get; set; } = 8.3f;

    [Description("Радиус взаимодействия с дырой (метры). Нажмите [E], стоя рядом.")]
    public float InteractRadius { get; set; } = 1.8f;

    [Description("Шанс неудачи (урон и предмет остаётся) в процентах.")]
    public int FailChancePercent { get; set; } = 5;

    [Description("Урон при неудаче.")]
    public float FailDamage { get; set; } = 30f;

    [Description("Белый список предметов, которые можно получить из дыры.")]
    public List<ItemType> AllowedItems { get; set; } = new()
    {
        ItemType.KeycardJanitor, ItemType.KeycardScientist, ItemType.KeycardResearchCoordinator,
        ItemType.KeycardZoneManager, ItemType.KeycardGuard, ItemType.KeycardContainmentEngineer,
        ItemType.KeycardMTFPrivate, ItemType.KeycardMTFOperative, ItemType.KeycardMTFCaptain,
        ItemType.KeycardFacilityManager, ItemType.KeycardChaosInsurgency,
        ItemType.Radio, ItemType.GunCOM15, ItemType.Medkit, ItemType.Flashlight,
        ItemType.SCP500, ItemType.SCP207, ItemType.GrenadeHE, ItemType.GrenadeFlash,
        ItemType.GunFSP9, ItemType.SCP018, ItemType.SCP268, ItemType.Adrenaline,
        ItemType.Painkillers, ItemType.Coin, ItemType.SCP2176, ItemType.SCP1853,
        ItemType.AntiSCP207, ItemType.Lantern
    };
}

public sealed class ChatSayConfig
{
    [Description("Включен ли глобальный чат .say.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Сколько последних сообщений показывать в HUD.")]
    public int HistorySize { get; set; } = 6;

    [Description("Сколько секунд сообщения висят на экране.")]
    public float MessageLifetime { get; set; } = 12f;

    [Description("Кулдаун между сообщениями одного игрока (секунды).")]
    public float CooldownSeconds { get; set; } = 3f;

    [Description("Максимальная длина сообщения.")]
    public int MaxLength { get; set; } = 160;
}

public sealed class CallVoteConfig
{
    [Description("Включено ли голосование за рестарт раунда.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Длительность голосования (секунды).")]
    public float DurationSeconds { get; set; } = 30f;

    [Description("Кулдаун между голосованиями (секунды, глобальный).")]
    public float CooldownSeconds { get; set; } = 60f;

    [Description("Минимум живых игроков для старта голосования.")]
    public int MinimumAlivePlayers { get; set; } = 4;
}

public sealed class ScpSwapConfig
{
    [Description("Включен ли .swap для SCP.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Сколько секунд с начала раунда доступна смена роли.")]
    public float WindowSeconds { get; set; } = 90f;
}

public sealed class RemoteKeycardConfig
{
    [Description("Включен ли Remote Keycard.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Работают ли карты для дверей.")]
    public bool AllowDoors { get; set; } = true;

    [Description("Работают ли карты для шкафчиков.")]
    public bool AllowLockers { get; set; } = true;

    [Description("Показывать подсказку при открытии картой не из рук.")]
    public bool ShowHint { get; set; } = true;
}

public sealed class FacilityAuthConfig
{
    [Description("Включен ли Facility Auth (тесла игнорирует игроков с любой картой, кроме KeycardChaosInsurgency).")]
    public bool IsEnabled { get; set; } = true;
}

public sealed class ShowReportsConfig
{
    [Description("Включены ли уведомления о репортах.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Сообщение репортёру после отправки жалобы.")]
    public string ReporterMessage { get; set; } = "<color=#ffd285><b>Репорт</b></color> на {target} отправлен <color=#a3e635>админам и в дискорд</color>!";

    [Description("Длительность показа сообщения репортёру (секунды).")]
    public float ReporterMessageDuration { get; set; } = 10f;

    [Description("Длительность показа уведомления админам (секунды).")]
    public float AdminNotifyDuration { get; set; } = 10f;
}

public sealed class PlayerXpConfig
{
    [Description("Включена ли система опыта и уровней.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Делитель опыта (весь получаемый опыт делится на это число), как в оригинале Hazbin.")]
    public float XpDivisor { get; set; } = 3f;

    [Description("Множитель опыта для игроков с тегом в нике.")]
    public float TaggedMultiplier { get; set; } = 2f;

    [Description("Тег в нике для множителя опыта (регистр не важен).")]
    public string SpecialTag { get; set; } = "#капибара";

    [Description("Сколько раз в секундах начисляется опыт за жизнь.")]
    public float AliveTickSeconds { get; set; } = 60f;

    [Description("Опыт за один тик жизни.")]
    public float AliveXpAmount { get; set; } = 1f;

    [Description("Начислять ли опыт игрокам с включённым 'Do Not Track' (по умолчанию — нет, как в оригинале).")]
    public bool AwardDnt { get; set; } = false;

    [Description("Текст уровня для неизвестных/DNT игроков.")]
    public string UnknownText { get; set; } = "Неизвестно";

    [Description("HEX-цвет уровня для неизвестных/DNT игроков.")]
    public string UnknownColorHex { get; set; } = "#8a6f46";
}

public sealed class Scp294Config
{
    [Description("Включен ли SCP-294 (кофемашина с напитками).")]
    public bool IsEnabled { get; set; } = true;

    [Description("Имя схематики кофемашины.")]
    public string SchematicName { get; set; } = "SCP294";

    [Description("Комната, в которую спавнится машина при старте раунда большой двухэтажный офис EZ.")]
    public Exiled.API.Enums.RoomType SpawnRoom { get; set; } = Exiled.API.Enums.RoomType.EzUpstairsPcs;

    [Description("Смещение позиции машины относительно центра комнаты по X.")]
    public float OffsetX { get; set; } = 7.0f;

    [Description("Смещение позиции машины относительно центра комнаты по Y (EzUpstairsPcs — двухэтажный, второй этаж ~3.8м).")]
    public float OffsetY { get; set; } = 2.9f;

    [Description("Смещение позиции машины относительно центра комнаты по Z.")]
    public float OffsetZ { get; set; } = 2.4f;

    [Description("Поворот машины по оси Y относительно комнаты.")]
    public float RotationY { get; set; } = 270f;

    [Description("Радиус взаимодействия с машиной (метры). Нажмите [E], стоя рядом.")]
    public float InteractRadius { get; set; } = 2.2f;

    [Description("Кулдаун выдачи напитка на игрока (секунды) — машина 'перезагружается'.")]
    public float CooldownSeconds { get; set; } = 30f;
}

public sealed class Scp120Config
{
    [Description("Включен ли SCP-120 (Детский бассейн-телепорт и трансформация предметов).")]
    public bool IsEnabled { get; set; } = true;

    [Description("Имя схематики для спавна бассейна SCP-120.")]
    public string SchematicName { get; set; } = "SCP120";

    [Description("Автоматически спавнить SCP-120 в GlassBox (LCZ) при старте раунда.")]
    public bool AutoSpawnInGlassBox { get; set; } = true;

    [Description("Смещение позиции бассейна по оси X относительно центра комнаты GlassBox.")]
    public float OffsetX { get; set; } = 4.68f;

    [Description("Смещение позиции бассейна по оси Y относительно центра комнаты GlassBox.")]
    public float OffsetY { get; set; } = -0.06f;

    [Description("Смещение позиции бассейна по оси Z относительно центра комнаты GlassBox.")]
    public float OffsetZ { get; set; } = 2.32f;

    [Description("Поворот бассейна по оси Y относительно комнаты GlassBox.")]
    public float RotationY { get; set; } = 180.0f;

    [Description("Кулдаун между телепортациями игроков и трансформациями предметов в секундах.")]
    public float TeleportCooldown { get; set; } = 1.5f;

    [Description("Радиус обнаружения игрока в бассейне (метры, 2D по горизонтали).")]
    public float PlayerDetectRadius { get; set; } = 1.8f;

    [Description("Радиус обнаружения предметов в воде бассейна (метры, 2D по горизонтали).")]
    public float ItemDetectRadius { get; set; } = 1.45f;

    [Description("Шанс телепортации на Поверхность вместо комнаты комплекса в процентах (0-100).")]
    public int SurfaceChancePercent { get; set; } = 7;

    [Description("Шанс повторного реролла легендарного предмета (VeryRare -> VeryRare) в процентах. Остаток — понижение до Rare.")]
    public int LegendaryRerollChance { get; set; } = 40;

    [Description("Задержка перед автоматическим закрытием всех дверей комнаты GlassBox после старта раунда (секунды).")]
    public float DoorsCloseDelaySeconds { get; set; } = 5f;

    [Description("Дополнительный допуск к радиусу при проверке зоны перед самой телепортацией (множитель). Игрок, вышедший из зоны во время погружения, остаётся на месте.")]
    public float CancelZoneMultiplier { get; set; } = 1.3f;
}

public sealed class DotResKillConfig
{
    [Description("Включена ли команда .res для наблюдателей в начале раунда.")]
    public bool ResEnabled { get; set; } = true;

    [Description("Сколько секунд с начала раунда доступна команда .res (по умолчанию 180 секунд = 3 минуты).")]
    public float ResWindowSeconds { get; set; } = 180f;

    [Description("Кулдаун на использование команды .res в секундах.")]
    public float ResCooldownSeconds { get; set; } = 10f;

    [Description("Шанс возрождения за Класс D в % (остаток — за Учёного).")]
    public float ResClassDChance { get; set; } = 75f;

    [Description("Включена ли команда .kill для самоубийства живых игроков.")]
    public bool KillEnabled { get; set; } = true;

    [Description("Кулдаун на использование команды .kill в секундах.")]
    public float KillCooldownSeconds { get; set; } = 10f;

    [Description("Случайные забавные причины смерти при вызове .kill.")]
    public List<string> KillReasons { get; set; } = new()
    {
        "Забанен за нарушение 0-го правила: \"Не умирать\"",
        "Превышена допустимая концентрация кринжа в крови",
        "Ваши жизненные показатели не найдены",
        "Скончался от скуки во время лекции",
        "Неудачная синхронизация с ритмом Вихря",
        "Сингулярность поглотила ваши координаты",
        "Гравитационный скачок: внутренние органы поменялись местами",
        "Внезапная спонтанная дезинтеграция ДНК",
        "Квантовая суперпозиция: вы одновременно живы и мертвы"
    };
}

public sealed class NoRulesFriendlyFireConfig
{
    [Description("Включать ли Friendly Fire автоматически при завершении раунда.")]
    public bool EnableAtRoundEnd { get; set; } = true;

    [Description("HUD-оповещение при включении резни в конце раунда.")]
    public string RoundEndMessage { get; set; } = "<color=#f87171><b>РЕЖИМ FRIENDLY FIRE ВКЛЮЧЕН!</b></color>\n<color=#c2c2c2>Да будет резня!</color>";
}

public sealed class HitmarkerConfig
{
    [Description("Включены ли хитмаркеры при попадании по врагам.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Отображать всплывающее количество нанесенного урона.")]
    public bool ShowDamageNumber { get; set; } = true;
}

public sealed class InfinityStuffConfig
{
    [Description("Бесконечная батарея у раций (расход энергии = 0).")]
    public bool InfiniteRadio { get; set; } = true;

    [Description("Бесконечный запас патронов при перезарядке любого огнестрельного оружия.")]
    public bool InfiniteAmmo { get; set; } = true;

    [Description("Автоматически удалять выброшенные на пол патроны для уменьшения лагов.")]
    public bool RemoveAmmoDrops { get; set; } = true;
}

public sealed class IntercomListConfig
{
    [Description("Включено ли отображение живого списка выживших на экране Интеркома.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Интервал обновления экрана Интеркома в секундах.")]
    public float UpdateInterval { get; set; } = 1.0f;
}

public sealed class PinkCandyConfig
{
    [Description("Включен ли спавн Розовой Конфеты (CandyKindID.Pink) в вазочке SCP-330.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Шанс получения розовой конфеты в процентах (0 - 100).")]
    public int PinkCandyChance { get; set; } = 35;

    [Description("Бонус к шансу за каждую ранее съеденную розовую конфету в этом раунде (в процентах).")]
    public int ProgressiveBonusPercent { get; set; } = 25;
}

public sealed class BetterCoinsConfig
{
    [Description("Включена ли механика телепортации при подбрасывании монетки.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Шанс успешной телепортации в случайную комнату в процентах (0 - 100, по умолчанию 60% успех / 40% неудача). При неудаче монетка исчезает.")]
    public int TeleportChance { get; set; } = 60;

    [Description("Шанс спавна Класса-D с монеткой в инвентаре (в процентах).")]
    public int ClassDSpawnWithCoinChance { get; set; } = 25;
}

public sealed class BetterEscapeConfig
{
    [Description("Включена ли система кастомных сценариев побега.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Список сценариев побега: какая роль, в наручниках или без, в кого превращается.")]
    public List<EscapeScenarioModel> Scenarios { get; set; } = new()
    {
        new EscapeScenarioModel { OldRole = RoleTypeId.ClassD, NewRole = RoleTypeId.ChaosConscript, IsCuffed = false },
        new EscapeScenarioModel { OldRole = RoleTypeId.ClassD, NewRole = RoleTypeId.NtfPrivate, IsCuffed = true },
        new EscapeScenarioModel { OldRole = RoleTypeId.Scientist, NewRole = RoleTypeId.NtfSpecialist, IsCuffed = false },
        new EscapeScenarioModel { OldRole = RoleTypeId.Scientist, NewRole = RoleTypeId.ChaosConscript, IsCuffed = true },
        new EscapeScenarioModel { OldRole = RoleTypeId.FacilityGuard, NewRole = RoleTypeId.NtfSergeant, IsCuffed = false },
        new EscapeScenarioModel { OldRole = RoleTypeId.FacilityGuard, NewRole = RoleTypeId.ChaosConscript, IsCuffed = true }
    };
}

public sealed class EscapeScenarioModel
{
    public RoleTypeId OldRole { get; set; }
    public RoleTypeId NewRole { get; set; }
    public bool IsCuffed { get; set; }
}

public sealed class CapybaraPetConfig
{
    [Description("Включен ли летающий питомец-капибара.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Название схематики в MapEditorReborn.")]
    public string SchematicName { get; set; } = "Capybara";

    [Description("Масштаб капибары (0.35 - 0.5 — идеальный компактный размер питомца).")]
    public float Scale { get; set; } = 0.4f;

    [Description("Список SteamID игроков, за которыми летает капибара.")]
    public List<string> OwnerSteamIds { get; set; } = new()
    {
        "76561198708583029"
    };
}

public sealed class HackersConfig
{
    [Description("Включен ли концепт «Хакеры».")]
    public bool IsEnabled { get; set; } = true;

    [Description("Шанс замены волны на группировку Хакеров (в процентах), если СО2 не взял волну.")]
    public int ChancePercent { get; set; } = 30;

    [Description("Минимум спектаторов для формирования группировки.")]
    public int MinSpectators { get; set; } = 3;

    [Description("Максимальный размер группировки.")]
    public int SquadSizeMax { get; set; } = 6;

    [Description("Комната, в которой строится панель взлома.")]
    public string PanelRoom { get; set; } = "HczServers";

    [Description("Смещение панели относительно центра комнаты по X.")]
    public float OffsetX { get; set; } = 0f;

    [Description("Смещение панели относительно центра комнаты по Y.")]
    public float OffsetY { get; set; } = 0f;

    [Description("Смещение панели относительно центра комнаты по Z.")]
    public float OffsetZ { get; set; } = -1.5f;

    [Description("Радиус взаимодействия с панелью (метры).")]
    public float PanelRadius { get; set; } = 2.2f;

    [Description("Сколько секунд длится один тик взлома.")]
    public float HackTickSeconds { get; set; } = 1f;

    [Description("Сколько тиков взлома нужно (тик = HackTickSeconds).")]
    public int HackTicksRequired { get; set; } = 75;

    [Description("Дальше этого радиуса от панели прогресс сбрасывается (метры).")]
    public float HackRadius { get; set; } = 7f;

    [Description("Обратный отсчёт Omega Warhead после взлома (секунды).")]
    public float OmegaCountdownSeconds { get; set; } = 240f;

    [Description("Опыт за успешный взлом (без делителя).")]
    public float MissionXp { get; set; } = 300f;

    [Description("CASSIE-оповещение при вторжении (на 50% взлома).")]
    public string CassieAlert { get; set; } = "ATTENTION . UNAUTHORIZED ACCESS TO CONTROL SYSTEMS DETECTED";
}

public sealed class Scp008Config
{
    [Description("Включен ли концепт «SCP-008».")]
    public bool IsEnabled { get; set; } = true;

    [Description("Радиус взаимодействия с трубкой (метры).")]
    public float TubeRadius { get; set; } = 2.2f;

    [Description("Смещение трубок относительно центра комнаты по X.")]
    public float OffsetX { get; set; } = 0f;

    [Description("Смещение трубок относительно центра комнаты по Y.")]
    public float OffsetY { get; set; } = 0f;

    [Description("Смещение трубок относительно центра комнаты по Z.")]
    public float OffsetZ { get; set; } = -4f;

    [Description("Сколько секунд после открытия трубки до начала вспышки вируса.")]
    public float OpenDurationToOutbreak { get; set; } = 45f;

    [Description("Периодичность тика урона вируса (секунды).")]
    public float TickSeconds { get; set; } = 10f;

    [Description("Урон людям за один тик за каждую открытую трубку.")]
    public float OutbreakDamagePerTick { get; set; } = 2f;

    [Description("Лечить ли SCP-сторону на каждом тике вспышки.")]
    public bool HealScpsOnTick { get; set; } = true;

    [Description("Сколько HP получает каждый SCP за тик (если HealScpsOnTick).")]
    public float ScpHealAmount { get; set; } = 15f;

    [Description("CASSIE-сообщение при открытии трубке.")]
    public string CassieOutbreak { get; set; } = "DANGER . VIRUS SCP 0 0 8 CONTAINMENT BREACH DETECTED";
}

public sealed class AirDropConfig
{
    [Description("Включен ли концепт «AirDrop».")]
    public bool IsEnabled { get; set; } = true;

    [Description("Задержка первого прилёта после старта раунда (секунды).")]
    public float FirstDelaySeconds { get; set; } = 200f;

    [Description("Интервал между прилётами (секунды).")]
    public float IntervalSeconds { get; set; } = 200f;

    [Description("Предметы, которые сбрасывает самолёт.")]
    public System.Collections.Generic.List<ItemType> DropItems { get; set; } = new()
    {
        ItemType.Medkit, ItemType.Adrenaline, ItemType.SCP207,
        ItemType.ArmorCombat, ItemType.GunFSP9, ItemType.GunCOM18,
        ItemType.SCP500, ItemType.Flashlight, ItemType.Radio
    };
}