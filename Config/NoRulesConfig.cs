using System.ComponentModel;
using Exiled.API.Interfaces;

namespace Capy.NoRules.Config;

/// <summary>
/// Главная конфигурация серверного плагина CapyLib.NoRules.
/// </summary>
public sealed class NoRulesConfig : IConfig
{
    [Description("Включен ли плагин NoRules.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Режим подробного логирования для отладки.")]
    public bool Debug { get; set; } = false;

    [Description("Настройки хитмаркеров и всплывающего урона при стрельбе.")]
    public HitmarkerConfig Hitmarkers { get; set; } = new();

    [Description("Настройки экранных HUD-оповещений (вместо стандартных бродкастов).")]
    public AnnouncementsConfig Announcements { get; set; } = new();
}

public sealed class HitmarkerConfig
{
    [Description("Включены ли хитмаркеры при попадании по врагам.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Отображать всплывающее количество нанесенного урона.")]
    public bool ShowDamageNumber { get; set; } = true;
}

public sealed class AnnouncementsConfig
{
    [Description("Приветственное HUD-оповещение при входе игрока на сервер (%player_name% заменится на ник).")]
    public string WelcomeMessage { get; set; } = "<color=#ffa94e>Добро пожаловать на <b>Капибара | NoRules</b>!</color>\n<color=#c2c2c2>Консоль: <color=#ffd285>.help</color> • Статистика: <color=#ffd285>.stats</color></color>";

    [Description("Длительность приветственного сообщения в секундах.")]
    public float WelcomeDuration { get; set; } = 6f;

    [Description("HUD-оповещение в начале раунда.")]
    public string RoundStartMessage { get; set; } = "<color=#ffa94e><b>РАУНД НАЧАЛСЯ!</b></color>\n<color=#c2c2c2>Удачи всем участникам комплекса Site-02</color>";

    [Description("Длительность оповещения начала раунда в секундах.")]
    public float RoundStartDuration { get; set; } = 5f;

    [Description("Показывать HUD-сводку победителей при окончании раунда.")]
    public bool ShowRoundEndSummary { get; set; } = true;
}
