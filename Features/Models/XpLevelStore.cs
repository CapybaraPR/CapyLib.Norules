using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Capy.NoRules.Features.Models;
using Exiled.API.Features;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Capy.NoRules.Features.Models;

/// <summary>
/// Загрузчик порогов уровней из Configs/CapyLib/PlayerXp/levels.yml.
/// Сам опыт хранится в общей БД CapyLib (PlayerDataModel.Xp) вместе со статистикой.
/// </summary>
public sealed class XpLevelStore
{
    private readonly string _levelsFile;
    private List<XpLevel> _levels = new();

    public static readonly List<XpLevel> DefaultLevels = new()
    {
        new() { MinXp = 0, Text = "Детёныш Капибары", ColorHex = "#b0b0b0" },
        new() { MinXp = 250, Text = "Юная Капибара", ColorHex = "#38bdf8" },
        new() { MinXp = 1000, Text = "Капибара", ColorHex = "#4ade80" },
        new() { MinXp = 2500, Text = "Матёрый Капибар", ColorHex = "#facc15" },
        new() { MinXp = 5000, Text = "Альфа Капибара", ColorHex = "#ef4444" },
        new() { MinXp = 10000, Text = "Хранитель Апельсинки", ColorHex = "#ff9f1c" },
        new() { MinXp = 25000, Text = "Царь Капибар", ColorHex = "#e879f9" },
        new() { MinXp = 50000, Text = "Легенда Капибар", ColorHex = "#ffd700" }
    };

    public IReadOnlyList<XpLevel> Levels => _levels;

    public XpLevelStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _levelsFile = Path.Combine(directory, "levels.yml");

        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_levelsFile))
            {
                File.WriteAllText(_levelsFile, CreateSerializer().Serialize(new LevelsFile { Levels = DefaultLevels }));
            }

            var loaded = CreateDeserializer().Deserialize<LevelsFile>(File.ReadAllText(_levelsFile));
            if (loaded?.Levels is { Count: > 0 })
            {
                _levels = loaded.Levels.OrderByDescending(l => l.MinXp).ToList();
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[PlayerXp] Ошибка чтения levels.yml: {ex.Message}");
        }

        _levels = DefaultLevels.OrderByDescending(l => l.MinXp).ToList();
    }

    private static ISerializer CreateSerializer() => new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static IDeserializer CreateDeserializer() => new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Уровень по количеству опыта (последний подходящий порог).</summary>
    public XpLevel GetLevelForXp(float xp)
    {
        return _levels.FirstOrDefault(l => xp >= l.MinXp) ?? _levels[0];
    }
}
