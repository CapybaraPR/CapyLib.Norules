using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Capy.Engine.CustomItems.Manager;
using CommandSystem;
using Exiled.API.Features;
using Exiled.CustomItems.API.Features;

namespace Capy.NoRules.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class GiveCustomItemCommand : ICommand
{
    public string Command => "givecustomitem";
    public string[] Aliases => new[] { "gci", "giveitem" };
    public string Description => "Выдать кастомный предмет игроку (с поддержкой поиска по префиксу/буквам).";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        // 1. Если аргументов нет — выводим список всех предметов
        if (arguments.Count < 1 || arguments.At(0).Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            response = BuildItemsList();
            return true;
        }

        string query = arguments.At(0).ToLowerInvariant();

        // 2. Определение целевого игрока
        Player? target = null;
        if (arguments.Count >= 2)
        {
            string playerQuery = arguments.At(1);
            target = Player.Get(playerQuery);
            if (target == null)
            {
                response = $"<color=red>[ОШИБКА]</color> Игрок '{playerQuery}' не найден.";
                return false;
            }
        }
        else
        {
            target = Player.Get(sender);
            if (target == null)
            {
                response = "<color=red>[ОШИБКА]</color> Не удалось определить игрока. Укажите ID или никнейм цели вторым аргументом.";
                return false;
            }
        }

        // 3. Поиск предметов по префиксу / имени
        var allExiledItems = CustomItem.Registered.ToList();
        var allCapyItems = CustomItemsManager.Items.ToList();

        // Точное совпадение по имени или ID
        var exactExiled = allExiledItems.FirstOrDefault(i => 
            i.Name.Equals(query, StringComparison.OrdinalIgnoreCase) || 
            i.Id.ToString() == query);

        if (exactExiled != null)
        {
            exactExiled.Give(target);
            response = $"<color=green>[УСПЕХ]</color> Предмет <b>{exactExiled.Name}</b> (ID: {exactExiled.Id}) выдан игроку <b>{target.Nickname}</b>.";
            return true;
        }

        var exactCapy = allCapyItems.FirstOrDefault(i => 
            i.Name.Equals(query, StringComparison.OrdinalIgnoreCase));

        if (exactCapy != null)
        {
            exactCapy.Give(target);
            response = $"<color=green>[УСПЕХ]</color> Предмет <b>{exactCapy.Name}</b> выдан игроку <b>{target.Nickname}</b>.";
            return true;
        }

        // Поиск по префиксу (начинается с query)
        var prefixMatchesExiled = allExiledItems
            .Where(i => i.Name.ToLowerInvariant().StartsWith(query))
            .ToList();

        var prefixMatchesCapy = allCapyItems
            .Where(i => i.Name.ToLowerInvariant().StartsWith(query))
            .ToList();

        int totalPrefixCount = prefixMatchesExiled.Count + prefixMatchesCapy.Count;

        if (totalPrefixCount == 1)
        {
            if (prefixMatchesExiled.Count == 1)
            {
                var item = prefixMatchesExiled[0];
                item.Give(target);
                response = $"<color=green>[УСПЕХ]</color> Предмет <b>{item.Name}</b> (ID: {item.Id}) выдан игроку <b>{target.Nickname}</b>.";
                return true;
            }
            else
            {
                var item = prefixMatchesCapy[0];
                item.Give(target);
                response = $"<color=green>[УСПЕХ]</color> Предмет <b>{item.Name}</b> выдан игроку <b>{target.Nickname}</b>.";
                return true;
            }
        }

        // Частичный поиск (содержит query)
        var containsMatchesExiled = allExiledItems
            .Where(i => i.Name.ToLowerInvariant().Contains(query))
            .ToList();

        var containsMatchesCapy = allCapyItems
            .Where(i => i.Name.ToLowerInvariant().Contains(query))
            .ToList();

        var allMatches = new List<string>();
        allMatches.AddRange(prefixMatchesExiled.Select(i => $"[Exiled] {i.Name} (ID: {i.Id})"));
        allMatches.AddRange(prefixMatchesCapy.Select(i => $"[Capy] {i.Name}"));

        if (allMatches.Count == 0)
        {
            allMatches.AddRange(containsMatchesExiled.Select(i => $"[Exiled] {i.Name} (ID: {i.Id})"));
            allMatches.AddRange(containsMatchesCapy.Select(i => $"[Capy] {i.Name}"));
        }

        if (allMatches.Count > 1)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<color=yellow>[ВНИМАНИЕ]</color> По запросу '<b>{query}</b>' найдено {allMatches.Count} предметов:");
            foreach (var match in allMatches)
            {
                sb.AppendLine($"  <color=#ffd285>• {match}</color>");
            }
            sb.Append("Уточните название предмета или укажите его точный ID.");
            response = sb.ToString();
            return false;
        }

        response = $"<color=red>[ОШИБКА]</color> Предмет по запросу '<b>{query}</b>' не найден.\nИспользуйте <b>gci list</b> для просмотра доступных предметов.";
        return false;
    }

    private static string BuildItemsList()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("<color=#ffa94e>====================================================</color>");
        sb.AppendLine("<b><color=#ffd285>       [ СПИСОК ДОСТУПНЫХ КАСТОМНЫХ ПРЕДМЕТОВ ]</color></b>");
        sb.AppendLine("<color=#ffa94e>====================================================</color>");

        var exiledItems = CustomItem.Registered.ToList();
        var capyItems = CustomItemsManager.Items.ToList();

        if (exiledItems.Count == 0 && capyItems.Count == 0)
        {
            sb.AppendLine("  <color=#c2c2c2>В данный момент кастомные предметы не зарегистрированы.</color>");
        }
        else
        {
            foreach (var item in capyItems)
            {
                sb.AppendLine($"  <color=#a3e635>• [Capy]</color> <color=#ffd285><b>{item.Name}</b></color> <color=#c2c2c2>({item.BaseType})</color>");
            }
            foreach (var item in exiledItems)
            {
                sb.AppendLine($"  <color=#38bdf8>• [ID: {item.Id}]</color> <color=#ffd285><b>{item.Name}</b></color> <color=#c2c2c2>({item.Type})</color>");
            }
        }

        sb.AppendLine("<color=#ffa94e>====================================================</color>");
        sb.AppendLine("<color=#c2c2c2>Использование: <color=#ffd285>gci <название/ID/префикс> [ID_игрока]</color></color>");
        sb.Append("<color=#ffa94e>====================================================</color>");

        return sb.ToString();
    }
}
