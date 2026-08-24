namespace Capy.NoRules.Features.Models;

/// <summary>
/// Запись игрока в хранилище опыта (players.yml).
/// </summary>
public sealed class XpRecord
{
    [YamlDotNet.Serialization.YamlMember(Alias = "userid")]
    public string UserId { get; set; } = string.Empty;

    [YamlDotNet.Serialization.YamlMember(Alias = "xp")]
    public float Xp { get; set; }

    [YamlDotNet.Serialization.YamlMember(Alias = "nickname")]
    public string Nickname { get; set; } = string.Empty;
}

/// <summary>
/// Корневой контейнер players.yml.
/// </summary>
public sealed class PlayersFile
{
    [YamlDotNet.Serialization.YamlMember(Alias = "players")]
    public List<XpRecord> Players { get; set; } = new();
}
