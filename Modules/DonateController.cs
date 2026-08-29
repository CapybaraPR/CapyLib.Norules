using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Enum;
using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;
using UnityEngine;

namespace Capy.NoRules.Modules;

public sealed class UserDonateRankResponse
{
    [JsonPropertyName("rank")]
    public string? Rank { get; set; }

    [JsonPropertyName("tag_text")]
    public string? TagText { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

public sealed class DonateMarketRequest
{
    [JsonPropertyName("steam_id")]
    public string SteamId { get; set; } = string.Empty;

    [JsonPropertyName("item_type")]
    public string ItemType { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; } = 1;
}

/// <summary>
/// Система интеграции доната:
/// 1. Синхронизация донат-рангов и бейджей с веб-сервером (RankSync).
/// 2. Выдача купленных предметов из инвентаря сайта игрокам через команду .get (MarketClaim).
/// </summary>
public sealed class DonateControllerFeature
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly DonateControllerConfig _config;
    private bool _enabled;
    private CoroutineHandle _updateCoroutine;

    public DonateControllerFeature(DonateControllerConfig config)
    {
        _config = config;
    }

    public void Enable()
    {
        if (_enabled || !_config.IsEnabled) return;
        _enabled = true;

        Exiled.Events.Handlers.Player.Verified += OnPlayerVerified;

        if (_config.UpdateInterval > 0f)
        {
            _updateCoroutine = Timing.RunCoroutine(AutoUpdateLoop());
        }

        Log.Info("[DonateController] Модуль синхронизации доната и инвентаря сайта успешно включен.");
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        Exiled.Events.Handlers.Player.Verified -= OnPlayerVerified;

        if (_updateCoroutine.IsRunning)
            Timing.KillCoroutines(_updateCoroutine);
    }

    private void OnPlayerVerified(VerifiedEventArgs ev)
    {
        if (ev.Player == null || ev.Player.IsNPC || !ev.Player.IsVerified) return;

        if (IsGroupProtected(ev.Player)) return;

        _ = CheckAndApplyRankAsync(ev.Player);
    }

    private bool IsGroupProtected(Player player)
    {
        string group = player.GroupName?.ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(group)) return false;

        return _config.IgnoredGroups.Any(g => !string.IsNullOrWhiteSpace(g) && group.Contains(g.ToLowerInvariant()));
    }

    private IEnumerator<float> AutoUpdateLoop()
    {
        while (_enabled)
        {
            yield return Timing.WaitForSeconds(_config.UpdateInterval);

            foreach (Player player in Player.List)
            {
                if (player == null || !player.IsConnected || player.IsNPC || !player.IsVerified)
                    continue;

                if (IsGroupProtected(player))
                    continue;

                _ = CheckAndApplyRankAsync(player);
            }
        }
    }

    public async Task<bool> CheckAndApplyRankAsync(Player player)
    {
        try
        {
            if (player == null || !player.IsConnected) return false;

            string cleanId = CleanUserId(player.UserId);
            if (string.IsNullOrEmpty(cleanId)) return false;

            string requestUrl = $"{_config.ApiUrl.TrimEnd('/')}/{_config.ServerType}/{cleanId}";

            HttpResponseMessage response = await HttpClient.GetAsync(requestUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (_config.Debug)
                    Log.Debug($"[DonateController] API вернул код {response.StatusCode} для игрока {player.Nickname} ({cleanId})");
                return false;
            }

            string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<UserDonateRankResponse>(content);
            if (data == null) return false;

            Timing.CallDelayed(0f, () => ApplyRankInternal(player, data));
            return true;
        }
        catch (Exception ex)
        {
            if (_config.Debug)
                Log.Debug($"[DonateController] Ошибка запроса к API доната: {ex.Message}");
            return false;
        }
    }

    private void ApplyRankInternal(Player player, UserDonateRankResponse data)
    {
        try
        {
            if (player == null || !player.IsConnected || player.ReferenceHub == null) return;
            if (IsGroupProtected(player)) return;

            if (!string.IsNullOrWhiteSpace(data.Rank))
            {
                var group = ServerStatic.PermissionsHandler.GetGroup(data.Rank);
                if (group != null && player.GroupName != data.Rank)
                {
                    player.ReferenceHub.serverRoles.SetGroup(group, false);
                    if (_config.Debug)
                        Log.Debug($"[DonateController] Установлена группа '{data.Rank}' для {player.Nickname}");
                }
            }

            if (!string.IsNullOrWhiteSpace(data.TagText))
            {
                player.RankName = data.TagText;
                player.RankColor = !string.IsNullOrWhiteSpace(data.Color) ? data.Color : "silver";
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[DonateController] Ошибка применения ранга: {ex}");
        }
    }

    public async Task ClaimMarketItemAsync(Player player, string itemInput, int amount)
    {
        try
        {
            if (player == null || !player.IsConnected) return;

            amount = Mathf.Clamp(amount, 1, 8);

            string cleanId = CleanUserId(player.UserId);
            var requestData = new DonateMarketRequest
            {
                SteamId = cleanId,
                ItemType = itemInput,
                Amount = amount
            };

            string json = JsonSerializer.Serialize(requestData);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage result = await HttpClient.PostAsync(_config.MarketApiUrl, content).ConfigureAwait(false);

            if (result.IsSuccessStatusCode)
            {
                Timing.CallDelayed(0f, () =>
                {
                    if (player == null || !player.IsConnected || !player.IsAlive) return;

                    if (Enum.TryParse<ItemType>(itemInput, true, out var itemType) && itemType != ItemType.None)
                    {
                        int given = 0;
                        for (int i = 0; i < amount; i++)
                        {
                            if (player.IsInventoryFull)
                                break;

                            player.AddItem(itemType);
                            given++;
                        }

                        if (given > 0)
                        {
                            player.ShowZoneHint(HintZone.TopCenter,
                                $"<color=#4ade80><b>🎁 ИНВЕНТАРЬ САЙТА</b></color>\n" +
                                $"<size=75%><color=#c2c2c2>Вы успешно забрали: <color=#ffd285><b>{given}x {itemType}</b></color></color></size>",
                                4.5f, "market_claim", 25);
                        }
                        else
                        {
                            player.ShowZoneHint(HintZone.TopCenter,
                                $"<color=#f59e0b><b>⚠️ ИНВЕНТАРЬ ПОЛОН</b></color>\n" +
                                $"<size=75%><color=#c2c2c2>Освободите место в инвентаре для получения предметов.</color></size>",
                                4f, "market_claim", 25);
                        }
                    }
                    else
                    {
                        player.ShowZoneHint(HintZone.TopCenter,
                            $"<color=#f59e0b><b>⚠️ ИНВЕНТАРЬ САЙТА</b></color>\n" +
                            $"<size=75%><color=#c2c2c2>Предмет <b>{itemInput}</b> списан, но не найден в игре.</color></size>",
                            4f, "market_claim", 25);
                    }
                });
            }
            else
            {
                Timing.CallDelayed(0f, () =>
                {
                    if (player == null || !player.IsConnected) return;

                    player.ShowZoneHint(HintZone.TopCenter,
                        $"<color=#ef4444><b>❌ ОШИБКА ВЫДАЧИ</b></color>\n" +
                        $"<size=75%><color=#c2c2c2>Предмета нет или недостаточно в инвентаре вашего профиля на сайте.</color></size>",
                        4f, "market_claim", 25);
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[DonateController] Ошибка Market API: {ex}");
            Timing.CallDelayed(0f, () =>
            {
                if (player != null && player.IsConnected)
                {
                    player.ShowZoneHint(HintZone.TopCenter,
                        "<color=#ef4444><b>❌ ОШИБКА СВЯЗИ С САЙТОМ</b></color>\n" +
                        "<size=75%><color=#c2c2c2>Не удалось связаться с сервером инвентаря. Попробуйте позже.</color></size>",
                        4f, "market_claim", 25);
                }
            });
        }
    }

    public static string CleanUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId)) return string.Empty;
        return userId.Replace("@steam", "").Replace("@discord", "").Replace("@northwood", "");
    }
}

