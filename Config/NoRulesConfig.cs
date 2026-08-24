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
