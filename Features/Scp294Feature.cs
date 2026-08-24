using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.Engine.ServerSpecific;
using Capy.Engine.Studio.Core;
using Capy.NoRules.Config;
using Exiled.API.Enums;
using Exiled.API.Extensions;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;
using UnityEngine;

namespace Capy.NoRules.Features;

/// <summary>
/// Напиток SCP-294: эффекты применяются при употреблении выданного стакана.
/// </summary>
public sealed class Scp294Drink
{
    public string Name { get; }
    public string Description { get; }
    public int Limit { get; }
    public string HintText { get; }
    public float HintDuration { get; }
    public List<(EffectType Type, byte Intensity, float Duration)> Effects { get; } = new();
    public Action<Player>? CustomAction { get; set; }

    public Scp294Drink(string name, string description, int limit, string hintText, float hintDuration = 3f, Action<Player>? customAction = null)
    {
        Name = name;
        Description = description;
        Limit = limit;
        HintText = hintText;
        HintDuration = hintDuration;
        CustomAction = customAction;
    }
}

/// <summary>
/// Реализация SCP-294 («Кофемашина»).
/// - При старте раунда спавнится схематика кофемашины в заданной комнате (по умолчанию EzOfficeLarge).
/// - Игрок выбирает напиток командой .drink и получает его, нажав [E] рядом с машиной.
/// - Выданный стакан (AntiSCP207) можно выпить — тогда срабатывают эффекты напитка.
/// - Кулдаун машины на игрока + лимит каждого напитка на раунд.
/// </summary>
public sealed class Scp294Feature
{
    private static readonly List<Scp294Drink> Drinks = new()
    {
        new("Вода", "На вид как вода", 5, "Вода как вода, ничего необычного"),
        new("Чай", "Горячий на вид", 5, "Сахар где?!"),
        new("Кола", "Санкционочка", 10, "Трамп(не сосал) исправь мои проблемы"),
        new("Cum", "МММ", 10, "<color=#f8fafc>Oh shit, i am sorry</color>",
            customAction: p => p.Health = Mathf.Max(1f, p.Health - 10f)),
        new("Водка", "Наше пойло", 5, "<color=#fbbf24>Хорошо пошла!</color>",
            customAction: p => p.Health = Mathf.Max(1f, p.Health - 20f)),
        new("Балтика 7", "Легендарный сорт", 11, "<color=#facc15>Теперь я скуф</color>")
        {
            Effects = { (EffectType.Invigorated, 1, 5f) }
        },
        new("Снайпер", "Бум, в голову", 4,
            "<color=#f87171>Да! Да нет, пап, я не чокнутый стрелок, я убийца!..\nРазница в том, что первое - хобби, а второе - работа!</color>", 5f)
        {
            Effects = { (EffectType.Scp1853, 255, 120f) }
        },
        new("Scp 207", "Ням", 8, "<color=#a3e635>Я скорость!</color>")
        {
            Effects = { (EffectType.Scp207, 1, 150f) }
        },
        new("Хилка", "Сделано на основе боярышника", 6, "<color=#4ade80>Простатит в прошлом!</color>",
            customAction: p => p.Heal(50f)),
        new("Анти Scp 207", "Шугар фри!", 10, "<color=#38bdf8>Я сила!</color>")
        {
            Effects = { (EffectType.AntiScp207, 1, 150f) }
        },
        new("Dungeon master", "напиток качков", 5, "<color=#c084fc>вас научили мужской дружбе</color>",
            customAction: p => p.Health = 10f),
        new("Адский сок", "Сделан на основе лавы и крови", 7, "<color=#fb923c>Ох,с дымком</color>")
        {
            Effects = { (EffectType.Slowness, 20, 10f) }
        },
        new("Бархатные тяги", "На пару минут ваша обувь становится бархатной", 4, "<color=#e879f9><b>Кефтеме</b></color>")
        {
            Effects = { (EffectType.SilentWalk, 100, 90f) }
        },
        new("Широкий Путин", "Внутри жидкий смокинг", 5, "<color=#60a5fa>Я ваш преZидент</color>",
            customAction: p => p.Scale = new Vector3(1.25f, 0.9f, 1.25f)),
        new("Агент рая", "За тобой уже выехали", 3, "<color=#fca5a5>Самоуничтожение через 5 секунд.</color>",
            customAction: p => Timing.CallDelayed(5f, () =>
            {
                if (p == null || !p.IsConnected || !p.IsAlive) return;
                p.ShowZoneHint(HintZone.Notification, "<color=#ef4444>Детонация выполнена</color>", 2f, "scp294_heaven", 20);
                try { Map.ExplodeEffect(p.Position, ProjectileType.FragGrenade); } catch { }
                try { p.Kill(DamageType.Explosion); } catch { p.Health = 0f; }
            })),
        new("Жидкое мясо", "Говядина в бутылке", 6, "<color=#f87171>Уххххх, на вкус не очень</color>",
            customAction: p => p.Health = 50f),
        new("Шампунь джумбайсемба", "Пахнет аНанАсОм", 7, "<color=#67e8f9>Нахер пить мыло? 0_0</color>")
        {
            Effects = { (EffectType.Flashed, 1, 10f) }
        },
        new("Байкал", "Напиток древних русов", 8, "<color=#38bdf8>Теперь ты славянин</color>")
        {
            Effects = { (EffectType.Slowness, 20, 20f) },
            CustomAction = p => p.Scale = Vector3.one * 1.25f
        },
        new("Ягуар", "Верните мой 2007", 4, "<color=#facc15>Энергетики вредны для здоровья</color>")
        {
            Effects = { (EffectType.MovementBoost, 30, 10f) },
            CustomAction = p => p.Heal(50f)
        },
        new("Трамбалон", "Только для сигм", 5, "<color=#4ade80>Чтобы стать большим большим качком</color>",
            customAction: p => p.AddAhp(65)),
        new("Лит Енерджи", "Уффф мишаня", 6, "<color=#fde047>Вошёл в кондиции</color>")
        {
            Effects = { (EffectType.FogControl, 1, 10f) }
        },
    };

    private readonly Scp294Config _config;

    // Выбор напитка: UserId -> индекс напитка
    private readonly ConcurrentDictionary<string, int> _selectedDrink = new();

    // Кулдаун машины: UserId -> время следующего доступного использования
    private readonly ConcurrentDictionary<string, DateTime> _nextUseTime = new();

    // Лимиты за раунд: (UserId, индекс напитка) -> счётчик
    private readonly Dictionary<(string UserId, int DrinkIndex), int> _roundUsage = new();

    // Отслеживание выданных стаканов: serial -> индекс напитка
    private readonly ConcurrentDictionary<ushort, int> _trackedCups = new();

    // Игроки, для которых машина прямо сейчас готовит напиток
    private readonly HashSet<int> _preparingPlayers = new();

    private Vector3? _machinePosition;
    private bool _enabled;

    private const string MachineAudioKey = "Capy294Machine";
    private const string ClipMaking = "making";
    private const string ClipDontMake = "dontmake";

    public Scp294Feature(Scp294Config config)
    {
        _config = config;
    }

    public IReadOnlyList<Scp294Drink> GetDrinks() => Drinks;

    public void Enable()
    {
        if (_enabled || !_config.IsEnabled) return;
        _enabled = true;

        AssKeybinds.OnKeybindPressed += OnKeybindPressed;
        Exiled.Events.Handlers.Player.UsedItem += OnUsedItem;
        Exiled.Events.Handlers.Player.ChangedItem += OnChangedItem;
        Exiled.Events.Handlers.Player.Left += OnLeft;
        Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
        Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        AssKeybinds.OnKeybindPressed -= OnKeybindPressed;
        Exiled.Events.Handlers.Player.UsedItem -= OnUsedItem;
        Exiled.Events.Handlers.Player.ChangedItem -= OnChangedItem;
        Exiled.Events.Handlers.Player.Left -= OnLeft;
        Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;

        ClearState();
    }

    private void ClearState()
    {
        _selectedDrink.Clear();
        _nextUseTime.Clear();
        _roundUsage.Clear();
        _trackedCups.Clear();
        _preparingPlayers.Clear();
        _machinePosition = null;

        DestroyMachineAudio();
    }

    // --- Аудио ---

    private AudioPlayer? GetOrCreateMachineAudio()
    {
        if (_machinePosition == null) return null;

        try
        {
            return AudioPlayer.CreateOrGet(MachineAudioKey, onIntialCreation: p =>
            {
                var speaker = p.AddSpeaker("Main", isSpatial: true, minDistance: 1f, maxDistance: 30f, volume: 2f);
                speaker.transform.position = _machinePosition.Value;
            });
        }
        catch
        {
            return null;
        }
    }

    private void DestroyMachineAudio()
    {
        try
        {
            if (AudioPlayer.TryGet(MachineAudioKey, out var ap))
                ap.Destroy();
        }
        catch { }
    }

    /// <summary>
    /// Пространственный звук кофемашины (идёт пока напиток готовится).
    /// </summary>
    // Сколько игроков прямо сейчас готовит напиток — звук глушим только когда все закончили
    private int _activePrepares;

    private void StartMakingSound()
    {
        try
        {
            _activePrepares++;
            if (_activePrepares > 1) return; // звук уже играет для другого игрока

            if (AudioClipStorage.AudioClips.ContainsKey(ClipMaking) && GetOrCreateMachineAudio() is { } ap)
            {
                ap.RemoveClipByName(ClipMaking);
                ap.AddClip(ClipMaking, destroyOnEnd: false);
            }
        }
        catch { }
    }

    private void StopMakingSound()
    {
        try
        {
            _activePrepares = Math.Max(0, _activePrepares - 1);
            if (_activePrepares > 0) return; // кто-то ещё готовит

            if (AudioPlayer.TryGet(MachineAudioKey, out var ap))
                ap.RemoveClipByName(ClipMaking);
        }
        catch { }
    }

    /// <summary>
    /// Голосовая подсказка лично игроку (не выбран напиток).
    /// </summary>
    private static void PlayDontMakeSound(Player player)
    {
        try
        {
            if (player == null || !player.IsConnected || !AudioClipStorage.AudioClips.ContainsKey(ClipDontMake))
                return;

            string key = $"Capy294Voice_{player.Id}";
            var ap = AudioPlayer.CreateOrGet(key, onIntialCreation: p =>
            {
                p.transform.parent = player.GameObject.transform;
                var speaker = p.AddSpeaker("Main", isSpatial: false, volume: 1.5f);
                speaker.transform.parent = player.Transform;
                speaker.transform.localPosition = Vector3.zero;
            });

            ap.AddClip(ClipDontMake, destroyOnEnd: true);

            Timing.CallDelayed(12f, () =>
            {
                try
                {
                    if (AudioPlayer.TryGet(key, out var stale) && stale.ClipsById.Count == 0)
                        stale.Destroy();
                }
                catch { }
            });
        }
        catch { }
    }

    private void OnRoundStarted()
    {
        ClearState();

        if (!_config.IsEnabled) return;

        try
        {
            var room = Room.List.FirstOrDefault(r => r.Type == _config.SpawnRoom);
            if (room == null)
            {
                Log.Warn($"[SCP-294] Комната {_config.SpawnRoom} не найдена — машина не заспавнена.");
                return;
            }

            var existing = SchematicLoader.SpawnedSchematics.FirstOrDefault(s => s.Name.Equals(_config.SchematicName, StringComparison.OrdinalIgnoreCase));
            var schematic = existing ?? MapManager.SpawnInRoom(
                room,
                _config.SchematicName,
                new Vector3(_config.OffsetX, _config.OffsetY, _config.OffsetZ),
                Quaternion.Euler(0f, _config.RotationY, 0f));

            if (schematic != null)
                _machinePosition = schematic.Position + Vector3.up * 1.1f;
            else
                Log.Warn($"[SCP-294] Не удалось заспавнить схематику '{_config.SchematicName}'.");
        }
        catch (Exception ex)
        {
            Log.Error($"[SCP-294] Ошибка спавна: {ex}");
        }
    }

    private void OnWaitingForPlayers() => ClearState();

    private void OnKeybindPressed(Player player, CustomKeybind keybind)
    {
        if (keybind != CustomKeybind.E)
            return;

        TryServe(player);
    }

    public bool IsNearMachine(Player player)
    {
        return _machinePosition.HasValue &&
               player != null &&
               (player.Position - _machinePosition.Value).sqrMagnitude <= _config.InteractRadius * _config.InteractRadius;
    }

    private void TryServe(Player player)
    {
        if (!_config.IsEnabled || player == null || !player.IsAlive || player.IsScp)
            return;

        if (!IsNearMachine(player))
            return;

        if (_preparingPlayers.Contains(player.Id))
            return;

        if (!_selectedDrink.TryGetValue(player.UserId, out int drinkIndex))
        {
            player.ShowZoneHint(HintZone.Notification, "<color=#facc15>Вы не выбрали напиток!\nПропишите в консоли [~]: <b>.drink</b></color>", 3f, "scp294", 20);
            PlayDontMakeSound(player);
            return;
        }

        if (_nextUseTime.TryGetValue(player.UserId, out var next) && DateTime.UtcNow < next)
        {
            player.ShowZoneHint(HintZone.Notification, $"<color=#f87171>Кофемашина <b>перезагружается</b>!\nПодождите {(int)(next - DateTime.UtcNow).TotalSeconds} сек.</color>", 2.5f, "scp294", 20);
            return;
        }

        var drink = Drinks[drinkIndex];

        var key = (player.UserId, drinkIndex);
        _roundUsage.TryGetValue(key, out int used);
        if (used >= drink.Limit)
        {
            player.ShowZoneHint(HintZone.Notification, $"<color=#ef4444>Кофемашина <b>больше не может</b> выдать вам {drink.Name}!</color>", 3f, "scp294", 20);
            return;
        }

        // Машина готовит напиток 5 секунд; если игрок отойдёт — приготовление отменяется
        _preparingPlayers.Add(player.Id);
        StartMakingSound();
        Timing.RunCoroutine(PrepareDrinkCoroutine(player, drinkIndex));
    }

    private IEnumerator<float> PrepareDrinkCoroutine(Player player, int drinkIndex)
    {
        var drink = Drinks[drinkIndex];

        try
        {
            const float stageTime = 1.0f;
            const int stages = 5;

            for (int stage = 1; stage <= stages; stage++)
            {
                player.ShowZoneHint(HintZone.Notification,
                    $"<color=#38bdf8>☕ SCP-294 готовит <b>{drink.Name}</b>{new string('.', stage)}</color>", 1.1f, "scp294_prep", 20);

                yield return Timing.WaitForSeconds(stageTime);

                if (!player.IsConnected || !player.IsAlive || !IsNearMachine(player))
                {
                    player.ShowZoneHint(HintZone.Notification, "<color=#f87171>☕ Вы отошли от машины — приготовление <b>отменено</b>.</color>", 2.5f, "scp294_prep", 20);
                    yield break;
                }
            }

            // Выдаём стакан (AntiSCP207 — визуально кружка) и отслеживаем его
            var item = player.AddItem(ItemType.AntiSCP207);
            if (item != null)
                _trackedCups[item.Serial] = drinkIndex;

            player.CurrentItem = item;

            _roundUsage[(player.UserId, drinkIndex)] = _roundUsage.TryGetValue((player.UserId, drinkIndex), out int used) ? used + 1 : 1;
            _nextUseTime[player.UserId] = DateTime.UtcNow.AddSeconds(Mathf.Max(0f, _config.CooldownSeconds));

            player.ShowZoneHint(HintZone.Notification, $"<color=#38bdf8>☕ SCP-294 выдал: <b>{drink.Name}</b>\n<size=14>Выпейте стакан ([ЛКМ]), чтобы употребить.</size></color>", 3f, "scp294", 20);
        }
        finally
        {
            StopMakingSound();
            _preparingPlayers.Remove(player.Id);
        }
    }

    /// <summary>
    /// Стакан пьётся ванильно (анимация + расход предмета), но ванильный эффект AntiSCP207
    /// срезаем — остаются только эффекты самого напитка.
    /// </summary>
    private void OnUsedItem(UsedItemEventArgs ev)
    {
        if (ev.Item == null || !_trackedCups.TryGetValue(ev.Item.Serial, out int drinkIndex))
            return;

        _trackedCups.TryRemove(ev.Item.Serial, out _);

        if (ev.Player == null || !ev.Player.IsAlive)
            return;

        var drink = Drinks[drinkIndex];
        ApplyDrink(ev.Player, drink);

        // Убираем ванильный бафф анти-колы, если напиток сам не даёт AntiScp207
        bool drinkGrantsAnti207 = drink.Effects.Any(e => e.Type == EffectType.AntiScp207);
        if (!drinkGrantsAnti207)
        {
            try { ev.Player.DisableEffect(EffectType.AntiScp207); } catch { }
        }
    }

    private void OnChangedItem(ChangedItemEventArgs ev)
    {
        if (ev.Player == null || ev.Item == null || !_trackedCups.TryGetValue(ev.Item.Serial, out int drinkIndex))
            return;

        var drink = Drinks[drinkIndex];
        ev.Player.ShowZoneHint(HintZone.Notification, $"<color=#38bdf8>Вы держите <b>{drink.Name}</b></color>\n<size=14><color=#c2c2c2>{drink.Description}</color></size>", 2.5f, "scp294_hold", 15);
    }

    private static void ApplyDrink(Player player, Scp294Drink drink)
    {
        foreach (var (type, intensity, duration) in drink.Effects)
        {
            try { player.EnableEffect(type, intensity, duration); } catch { }
        }

        drink.CustomAction?.Invoke(player);

        if (!string.IsNullOrEmpty(drink.HintText))
            player.ShowZoneHint(HintZone.Notification, drink.HintText, drink.HintDuration, "scp294_drink", 20);
    }

    private void OnLeft(LeftEventArgs ev)
    {
        if (ev.Player == null) return;

        _preparingPlayers.Remove(ev.Player.Id);
        _selectedDrink.TryRemove(ev.Player.UserId, out _);
        _nextUseTime.TryRemove(ev.Player.UserId, out _);

        var toRemove = _trackedCups.Keys.Where(serial =>
        {
            var pickup = Exiled.API.Features.Pickups.Pickup.Get(serial);
            return pickup == null || (pickup.PreviousOwner != null && pickup.PreviousOwner.UserId == ev.Player.UserId);
        }).ToList();

        foreach (ushort serial in toRemove)
            _trackedCups.TryRemove(serial, out _);
    }

    /// <summary>
    /// Выбор напитка по имени или номеру (для команды .drink). null — не найден.
    /// </summary>
    public Scp294Drink? SelectDrink(Player player, string query)
    {
        int index = int.TryParse(query, out int id) ? id : FindIndexByName(query);
        if (index < 0 || index >= Drinks.Count)
            return null;

        _selectedDrink[player.UserId] = index;
        return Drinks[index];
    }

    private static int FindIndexByName(string name)
    {
        for (int i = 0; i < Drinks.Count; i++)
        {
            if (Drinks[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return Drinks.FindIndex(d => d.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
