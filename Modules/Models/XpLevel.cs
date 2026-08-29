using YamlDotNet.Serialization;

namespace Capy.NoRules.Modules.Models;

/// <summary>
/// Уровень игрока: порог опыта, отображаемый титул и HEX-цвет.
/// </summary>
public sealed class XpLevel
{
    [YamlMember(Alias = "min_xp")]
    public float MinXp { get; set; }

    [YamlMember(Alias = "text")]
    public string Text { get; set; } = string.Empty;

    [YamlMember(Alias = "color_hex")]
    public string ColorHex { get; set; } = "#ffffff";
}

