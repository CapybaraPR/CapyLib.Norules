namespace Capy.NoRules.Features.Models;

/// <summary>
/// Корневой контейнер levels.yml.
/// </summary>
public sealed class LevelsFile
{
    [YamlDotNet.Serialization.YamlMember(Alias = "levels")]
    public List<XpLevel> Levels { get; set; } = new();
}
