using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Capy.NoRules.Features.Models;
using Exiled.API.Features;
using MEC;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Capy.NoRules.Features.Models;

/// <summary>
/// Хранилище опыта: Configs/CapyLib/PlayerXp/players.yml + levels.yml.
/// Автосохранение каждые 10 секунд при изменениях.
/// </summary>
public sealed class XpDatabase : IDisposable
{
    private readonly string _playersFile;
    private readonly string _levelsFile;
    private readonly Dictionary<string, XpRecord> _players = new();
    private List<XpLevel> _levels = new();
    private bool _dirty;
    private readonly object _lock = new();
    private CoroutineHandle _saveCoroutine;

    private static readonly List<XpLevel> DefaultLevels = new()
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

    public XpDatabase(string directory)
    {
        Directory.CreateDirectory(directory);
        _playersFile = Path.Combine(directory, "players.yml");
        _levelsFile = Path.Combine(directory, "levels.yml");

        LoadLevels();
        LoadPlayers();

        _saveCoroutine = Timing.RunCoroutine(SaveCoroutine());
    }

    private static ISerializer CreateSerializer() => new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static IDeserializer CreateDeserializer() => new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private void LoadLevels()
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

    private void LoadPlayers()
    {
        try
        {
            if (!File.Exists(_playersFile)) return;

            var loaded = CreateDeserializer().Deserialize<PlayersFile>(File.ReadAllText(_playersFile));
            if (loaded?.Players == null) return;

            foreach (var record in loaded.Players.Where(r => !string.IsNullOrEmpty(r.UserId)))
                _players[record.UserId] = record;
        }
        catch (Exception ex)
        {
            Log.Error($"[PlayerXp] Ошибка чтения players.yml: {ex.Message}");
        }
    }

    private IEnumerator<float> SaveCoroutine()
    {
        for (;;)
        {
            yield return Timing.WaitForSeconds(10f);
            Save();
        }
    }

    private void Save()
    {
        List<XpRecord> snapshot;
        lock (_lock)
        {
            if (!_dirty) return;
            snapshot = _players.Values.ToList();
            _dirty = false;
        }

        try
        {
            File.WriteAllText(_playersFile, CreateSerializer().Serialize(new PlayersFile { Players = snapshot }));
        }
        catch (Exception ex)
        {
            Log.Error($"[PlayerXp] Ошибка сохранения: {ex.Message}");
        }
    }

    public bool Contains(string userId)
    {
        lock (_lock)
            return _players.ContainsKey(userId);
    }

    public void EnsurePlayer(string userId, string nickname)
    {
        lock (_lock)
        {
            if (_players.TryGetValue(userId, out var rec))
            {
                if (!string.IsNullOrEmpty(nickname) && rec.Nickname != nickname)
                {
                    rec.Nickname = nickname;
                    _dirty = true;
                }

                return;
            }

            _players[userId] = new XpRecord { UserId = userId, Xp = 0, Nickname = nickname ?? string.Empty };
            _dirty = true;
        }
    }

    public float GetXp(string userId)
    {
        lock (_lock)
            return _players.TryGetValue(userId, out var r) ? r.Xp : 0f;
    }

    public string GetNickname(string userId)
    {
        lock (_lock)
            return _players.TryGetValue(userId, out var r) ? r.Nickname : string.Empty;
    }

    public void GiveXp(string userId, float amount)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(userId, out var rec))
                rec = _players[userId] = new XpRecord { UserId = userId };

            rec.Xp += amount;
            _dirty = true;
        }
    }

    public void SetXp(string userId, float amount)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(userId, out var rec))
                rec = _players[userId] = new XpRecord { UserId = userId };

            rec.Xp = amount;
            _dirty = true;
        }
    }

    /// <summary>
    /// Уровень по количеству опыта (последний подходящий порог). null — игрока нет в базе.
    /// </summary>
    public XpLevel? GetLevel(string userId)
    {
        lock (_lock)
        {
            return _players.TryGetValue(userId, out var r) ? GetLevelForXp(r.Xp) : null;
        }
    }

    public XpLevel? GetLevelForXp(float xp)
    {
        return _levels.FirstOrDefault(l => xp >= l.MinXp);
    }

    /// <summary>
    /// Топ игроков по опыту. Возвращает пары (запись, уровень).
    /// </summary>
    public List<(XpRecord Record, XpLevel Level)> GetTop(int count)
    {
        lock (_lock)
        {
            return _players.Values
                .OrderByDescending(r => r.Xp)
                .Take(count)
                .Select(r => (r, GetLevelForXp(r.Xp) ?? _levels[_levels.Count - 1]))
                .ToList();
        }
    }

    public void Dispose()
    {
        Timing.KillCoroutines(_saveCoroutine);

        lock (_lock)
            _dirty = true;

        Save();
    }
}
