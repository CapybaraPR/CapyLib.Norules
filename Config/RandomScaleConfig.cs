using System.ComponentModel;

namespace Capy.NoRules.Config;

/// <summary>
/// Настройки случайного масштабирования (роста) моделей игроков.
/// </summary>
public sealed class RandomScaleConfig
{
    [Description("Включено ли случайное масштабирование моделей.")]
    public bool IsEnabled { get; set; } = true;

    [Description("Минимальный коэффициент масштабирования (0.95 = 95% от оригинала).")]
    public float MinScale { get; set; } = 0.95f;

    [Description("Максимальный коэффициент масштабирования (1.05 = 105% от оригинала).")]
    public float MaxScale { get; set; } = 1.05f;

    [Description("Применять ли случайный рост к SCP-объектам (по умолчанию false во избежание проблем с хитбоксами).")]
    public bool AffectScps { get; set; } = false;

    [Description("Применять ли случайный рост к роли Tutorial (по умолчанию false, чтобы не влиять на лобби и vanish).")]
    public bool AffectTutorial { get; set; } = false;
}
