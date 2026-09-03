using System.ComponentModel;

namespace Capy.NoRules.Config;

/// <summary>
/// Настройки защиты связанных и обезоруженных пленных.
/// </summary>
public sealed class DisarmedProtectionConfig
{
    [Description("Включен ли модуль защиты связанных игроков.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Защищать ли связанных Class-D от стрельбы союзников и захватчиков.")]
    public bool ProtectClassD { get; set; } = true;

    [Description("Защищать ли связанных Учёных.")]
    public bool ProtectScientists { get; set; } = true;

    [Description("Разрешать ли человеку, который САМ связал пленного, убивать его.")]
    public bool AllowCaptorToKill { get; set; } = false;

    [Description("Защищать ли связанных пленных от урона SCP-объектов.")]
    public bool ProtectFromScps { get; set; } = false;

    [Description("Уведомлять ли атаковавшего хинтом о запрете атаки связанных пленных.")]
    public bool NotifyAttacker { get; set; } = true;

    [Description("Текст подсказки атаковавшему игроку.")]
    public string WarningMessage { get; set; } = "<color=#ef4444><b>⚠️ ЗАПРЕЩЕНО УБИВАТЬ СВЯЗАННЫХ ПЛЕННЫХ!</b></color>";
}
