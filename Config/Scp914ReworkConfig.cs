using System.ComponentModel;

namespace Capy.NoRules.Config;

/// <summary>
/// Настройки переработки обработки игроков в SCP-914 (SCP-914 Rework).
/// </summary>
public sealed class Scp914ReworkConfig
{
    [Description("Включен ли модуль SCP-914 Rework для игроков.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Шанс (в %) превращения человека в зомби SCP-049-2 на режиме Rough.")]
    public int ZombieMutationChance { get; set; } = 15;

    [Description("Шанс (в %) исцеления зомби SCP-049-2 обратно в человека (Class-D) на режиме Very Fine.")]
    public int ZombieCureChance { get; set; } = 15;

    [Description("Влияет ли SCP-914 на основных SCP (049, 106, 173, 096, 939 и т.д.). По умолчанию false во избежание багов.")]
    public bool AffectMainScps { get; set; } = false;
}
