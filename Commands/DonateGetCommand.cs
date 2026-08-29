using System;
using Capy.NoRules.Addons;
using Capy.NoRules.Modules;
using Capy.NoRules.Concepts;
using Capy.NoRules.Scps;
using CommandSystem;
using Exiled.API.Features;
using UnityEngine;

namespace Capy.NoRules.Commands;

/// <summary>
/// Команда забора купленных предметов из инвентаря сайта (.get / .items / .market).
/// </summary>
[CommandHandler(typeof(ClientCommandHandler))]
public sealed class DonateGetCommand : ICommand
{
    public string Command => "get";
    public string[] Aliases => new[] { "take", "items", "market", "donateget", "взять", "донат" };
    public string Description => "Забрать купленные предметы из инвентаря профиля на сайте.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        var donateFeature = NoRulesPlugin.Instance?.DonateController;
        var config = NoRulesPlugin.Instance?.Config?.DonateController;

        if (donateFeature == null || config == null || !config.IsMarketEnabled)
        {
            response = "<color=red>[ОШИБКА]</color> Система выдачи предметов из инвентаря сайта отключена на этом сервере.";
            return false;
        }

        Player? player = Player.Get(sender);
        if (player == null || !player.IsConnected)
        {
            response = "<color=red>[ОШИБКА]</color> Команда доступна только игрокам на сервере.";
            return false;
        }

        if (Round.ElapsedTime.TotalSeconds < config.MarketCooldownSeconds)
        {
            int waitTime = (int)Math.Ceiling(config.MarketCooldownSeconds - Round.ElapsedTime.TotalSeconds);
            response = $"<color=yellow>[ИНВЕНТАРЬ САЙТА]</color> Забор предметов будет доступен через <b>{waitTime}</b> сек (первые {Mathf.CeilToInt(config.MarketCooldownSeconds / 60f)} мин раунда).";
            return false;
        }

        if (arguments.Count < 1)
        {
            response = "\n<color=#ffa94e><b>🎁 === [ ИНВЕНТАРЬ САЙТА ] ===</b></color>\n" +
                       "<color=#a3e635>• .get <название_предмета> [количество]</color> -- Забрать предмет\n" +
                       "<color=#c2c2c2>Примеры: <color=#ffd285>.get Medkit 2</color> | <color=#ffd285>.get GunE11SR</color> | <color=#ffd285>.get KeycardO5</color></color>";
            return true;
        }

        string itemInput = arguments.At(0);
        string amountStr = arguments.Count > 1 ? arguments.At(1) : "1";

        if (!int.TryParse(amountStr, out int amount) || amount <= 0)
        {
            response = "<color=red>[ОШИБКА]</color> Количество должно быть положительным целым числом.";
            return false;
        }

        _ = donateFeature.ClaimMarketItemAsync(player, itemInput, amount);
        response = $"<color=green>[ИНВЕНТАРЬ САЙТА]</color> Запрос на получение <b>{amount}x {itemInput}</b> отправлен...";
        return true;
    }
}

