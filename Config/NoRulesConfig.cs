using System.Collections.Generic;
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

    [Description("Список портов серверов, на которых должен работать этот плагин (например, 7777). Если список пуст, плагин работает везде.")]
    public List<ushort> TargetPorts { get; set; } = new() { 7777 };

    [Description("Настройки системы автоматического закрытия дверей.")]
    public AutoDoorsConfig AutoDoors { get; set; } = new();

    [Description("Настройки системы быстрого связывания игроков (Fast Disarm).")]
    public FastDisarmConfig FastDisarm { get; set; } = new();

    [Description("Настройки хитмаркеров при нанесении урона.")]
    public HitmarkerConfig Hitmarkers { get; set; } = new();

    [Description("Настройки игровых оповещений (Broadcasts).")]
    public BroadcastsConfig Broadcasts { get; set; } = new();
}

public sealed class AutoDoorsConfig
{
    [Description("Включено ли автоматическое закрытие дверей.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Задержка перед закрытием двери (в секундах).")]
    public float CloseDelay { get; set; } = 3.5f;

    [Description("Игнорировать ворота Gate A / Gate B.")]
    public bool IgnoreGates { get; set; } = true;

    [Description("Игнорировать чекпоинты (Checkpoints).")]
    public bool IgnoreCheckpoints { get; set; } = true;
}

public sealed class FastDisarmConfig
{
    [Description("Включено ли улучшенное связывание.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Мгновенное связывание (без удержания клавиши).")]
    public bool InstantDisarm { get; set; } = false;
}

public sealed class HitmarkerConfig
{
    [Description("Включены ли хитмаркеры при попадании по врагам.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Отображать всплывающее количество нанесенного урона.")]
    public bool ShowDamageNumber { get; set; } = true;
}

public sealed class BroadcastsConfig
{
    [Description("Приветственный бродкаст при входе игрока на сервер (%player_name% заменится на ник).")]
    public string WelcomeMessage { get; set; } = "<color=#ffa94e>Добро пожаловать на <b>Капибара | NoRules</b>!</color>\n<color=#c2c2c2>Консоль: <color=#ffd285>.help</color> • Статистика: <color=#ffd285>.stats</color></color>";

    [Description("Длительность приветственного сообщения в секундах.")]
    public ushort WelcomeDuration { get; set; } = 6;

    [Description("Бродкаст в начале раунда.")]
    public string RoundStartMessage { get; set; } = "<color=#ffa94e><b>РАУНД НАЧАЛСЯ!</b></color>\n<color=#c2c2c2>Удачи всем участникам комплекса Site-02</color>";

    [Description("Длительность бродкаста начала раунда в секундах.")]
    public ushort RoundStartDuration { get; set; } = 5;

    [Description("Показывать сводку лучших игроков раунда при его окончании.")]
    public bool ShowRoundEndSummary { get; set; } = true;
}
