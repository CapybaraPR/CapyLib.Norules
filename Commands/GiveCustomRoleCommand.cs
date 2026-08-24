using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Capy.Engine.CustomRoles.Manager;
using CommandSystem;
using Exiled.API.Features;
using Exiled.CustomRoles.API.Features;

namespace Capy.NoRules.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class GiveCustomRoleCommand : ICommand
{
    public string Command => "givecustomrole";
    public string[] Aliases => new[] { "gcr", "giverole" };
    public string Description => "Выдать кастомную роль игроку (с поддержкой поиска по префиксу/буквам).";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        // 1. Если аргументов нет — выводим список всех ролей
        if (arguments.Count < 1 || arguments.At(0).Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            response = BuildRolesList();
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

        // 3. Поиск ролей по префиксу / имени
        var allExiledRoles = CustomRole.Registered.ToList();
        var allCapyRoles = CustomRolesManager.Roles.ToList();

        // Точное совпадение
        var exactExiled = allExiledRoles.FirstOrDefault(r => 
            r.Name.Equals(query, StringComparison.OrdinalIgnoreCase) || 
            r.Id.ToString() == query);

        if (exactExiled != null)
        {
            exactExiled.AddRole(target);
            response = $"<color=green>[УСПЕХ]</color> Роль <b>{exactExiled.Name}</b> (ID: {exactExiled.Id}) выдана игроку <b>{target.Nickname}</b>.";
            return true;
        }

        var exactCapy = allCapyRoles.FirstOrDefault(r => 
            r.Name.Equals(query, StringComparison.OrdinalIgnoreCase));

        if (exactCapy != null)
        {
            exactCapy.OnAssigned(target);
            response = $"<color=green>[УСПЕХ]</color> Роль <b>{exactCapy.Name}</b> выдана игроку <b>{target.Nickname}</b>.";
            return true;
        }

        // Поиск по префиксу (начинается с query)
        var prefixMatchesExiled = allExiledRoles
            .Where(r => r.Name.ToLowerInvariant().StartsWith(query))
            .ToList();

        var prefixMatchesCapy = allCapyRoles
            .Where(r => r.Name.ToLowerInvariant().StartsWith(query))
            .ToList();

        int totalPrefixCount = prefixMatchesExiled.Count + prefixMatchesCapy.Count;

        if (totalPrefixCount == 1)
        {
            if (prefixMatchesExiled.Count == 1)
            {
                var role = prefixMatchesExiled[0];
                role.AddRole(target);
                response = $"<color=green>[УСПЕХ]</color> Роль <b>{role.Name}</b> (ID: {role.Id}) выдана игроку <b>{target.Nickname}</b>.";
                return true;
            }
            else
            {
                var role = prefixMatchesCapy[0];
                role.OnAssigned(target);
                response = $"<color=green>[УСПЕХ]</color> Роль <b>{role.Name}</b> выдана игроку <b>{target.Nickname}</b>.";
                return true;
            }
        }

        // Частичный поиск (содержит query)
        var containsMatchesExiled = allExiledRoles
            .Where(r => r.Name.ToLowerInvariant().Contains(query))
            .ToList();

        var containsMatchesCapy = allCapyRoles
            .Where(r => r.Name.ToLowerInvariant().Contains(query))
            .ToList();

        var allMatches = new List<string>();
        allMatches.AddRange(prefixMatchesExiled.Select(r => $"[Exiled] {r.Name} (ID: {r.Id})"));
        allMatches.AddRange(prefixMatchesCapy.Select(r => $"[Capy] {r.Name}"));

        if (allMatches.Count == 0)
        {
            allMatches.AddRange(containsMatchesExiled.Select(r => $"[Exiled] {r.Name} (ID: {r.Id})"));
            allMatches.AddRange(containsMatchesCapy.Select(r => $"[Capy] {r.Name}"));
        }

        if (allMatches.Count > 1)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<color=yellow>[ВНИМАНИЕ]</color> По запросу '<b>{query}</b>' найдено {allMatches.Count} ролей:");
            foreach (var match in allMatches)
            {
                sb.AppendLine($"  <color=#ffd285>• {match}</color>");
            }
            sb.Append("Уточните название роли или укажите её точный ID.");
            response = sb.ToString();
            return false;
        }

        response = $"<color=red>[ОШИБКА]</color> Кастомная роль по запросу '<b>{query}</b>' не найдена.\nИспользуйте <b>gcr list</b> для просмотра доступных ролей.";
        return false;
    }

    private static string BuildRolesList()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("<color=#ffa94e>====================================================</color>");
        sb.AppendLine("<b><color=#ffd285>        [ СПИСОК ДОСТУПНЫХ КАСТОМНЫХ РОЛЕЙ ]</color></b>");
        sb.AppendLine("<color=#ffa94e>====================================================</color>");

        var exiledRoles = CustomRole.Registered.ToList();
        var capyRoles = CustomRolesManager.Roles.ToList();

        if (exiledRoles.Count == 0 && capyRoles.Count == 0)
        {
            sb.AppendLine("  <color=#c2c2c2>В данный момент кастомные роли не зарегистрированы.</color>");
        }
        else
        {
            foreach (var role in capyRoles)
            {
                sb.AppendLine($"  <color=#a3e635>• [Capy]</color> <color=#ffd285><b>{role.Name}</b></color> <color=#c2c2c2>(Базовая роль: {role.BaseRole})</color>");
            }
            foreach (var role in exiledRoles)
            {
                sb.AppendLine($"  <color=#38bdf8>• [ID: {role.Id}]</color> <color=#ffd285><b>{role.Name}</b></color> <color=#c2c2c2>(Базовая роль: {role.Role})</color>");
            }
        }

        sb.AppendLine("<color=#ffa94e>====================================================</color>");
        sb.AppendLine("<color=#c2c2c2>Использование: <color=#ffd285>gcr <название/ID/префикс> [ID_игрока]</color></color>");
        sb.Append("<color=#ffa94e>====================================================</color>");

        return sb.ToString();
    }
}
