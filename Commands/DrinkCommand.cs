using System;
using System.Linq;
using Capy.NoRules.Addons;
using Capy.NoRules.Modules;
using Capy.NoRules.Concepts;
using Capy.NoRules.Scps;
using CommandSystem;
using Exiled.API.Features;

namespace Capy.NoRules.Commands;

/// <summary>
/// Выбор напитка SCP-294: .drink [имя|номер].
/// </summary>
[CommandHandler(typeof(ClientCommandHandler))]
public sealed class DrinkCommand : ICommand
{
    public string Command => "drink";
    public string[] Aliases => new[] { "dr", "294" };
    public string Description => "Выбрать напиток для SCP-294 или посмотреть список напитков";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (Player.Get(sender) is not { } player)
        {
            response = "Вы не игрок.";
            return false;
        }

        var feature = NoRulesPlugin.Instance?.Scp294;
        if (feature == null || !feature.GetDrinks().Any())
        {
            response = "SCP-294 недоступен на этом сервере.";
            return false;
        }

        var drinks = feature.GetDrinks();

        if (arguments.Count <= 0)
        {
            string list = string.Join("\n", drinks.Select((d, i) =>
                $"  <color=#67e8f9>[{i}]</color> <color=#ffffff>{d.Name}</color> <color=#a3a3a3>(макс. {d.Limit})</color> — {d.Description}"));

            response = "\n<color=#38bdf8><b>☕ === [ SCP-294: Меню напитков ] ===</b></color>\n" +
                       list +
                       "\n\n<color=#a3e635>Выбор:</color> <b>.drink <номер|имя></b>\n" +
                       "<color=#c2c2c2>Затем подойдите к машине и нажмите [E], чтобы получить стакан.</color>";
            return true;
        }

        string query = arguments.At(0);
        var drink = feature.SelectDrink(player, query);
        if (drink == null)
        {
            response = $"<color=red>Напиток '{query}' не найден.</color> Введите <b>.drink</b> для списка.";
            return false;
        }

        response = $"<color=green>Вы выбрали <b>{drink.Name}</b>.</color> Подойдите к кофемашине и нажмите <b>[E]</b>, чтобы получить стакан.";
        return true;
    }
}

