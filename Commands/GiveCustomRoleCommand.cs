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
    public string Description => "Выдать кастомную роль или роль концепта игроку.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        // 1. Без аргументов или list — красивый компактный список
        if (arguments.Count < 1 || arguments.At(0).Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            response = BuildRolesList();
            return true;
        }

        string query = arguments.At(0).Trim().ToLowerInvariant();

        // 2. Определение целевого игрока
        Player? target = null;
        if (arguments.Count >= 2)
        {
            string playerQuery = arguments.At(1);
            target = Player.Get(playerQuery);
            if (target == null)
            {
                response = $"<color=#ef4444>✖</color> Игрок '<b>{playerQuery}</b>' не найден.";
                return false;
            }
        }
        else
        {
            target = Player.Get(sender);
            if (target == null)
            {
                response = "<color=#ef4444>✖</color> Укажите ID или ник игрока: <b>gcr hack [id]</b>.";
                return false;
            }
        }

        // 3. Быстрые роли концептов (Хакеры, СО₂)
        var conceptResult = TryGiveConceptRole(query, target);
        if (conceptResult != null)
        {
            response = conceptResult;
            return true;
        }

        // 4. Поиск обычных кастомных ролей (CapyLib & EXILED)
        var allExiledRoles = CustomRole.Registered.ToList();
        var allCapyRoles = CustomRolesManager.Roles.ToList();

        // Точное совпадение
        var exactExiled = allExiledRoles.FirstOrDefault(r => 
            r.Name.Equals(query, StringComparison.OrdinalIgnoreCase) || 
            r.Id.ToString() == query);

        if (exactExiled != null)
        {
            exactExiled.AddRole(target);
            response = $"<color=#4ade80>✔</color> Роль <b>{exactExiled.Name}</b> (ID: {exactExiled.Id}) выдана игроку <b>{target.Nickname}</b>.";
            return true;
        }

        var exactCapy = allCapyRoles.FirstOrDefault(r => 
            r.Name.Equals(query, StringComparison.OrdinalIgnoreCase));

        if (exactCapy != null)
        {
            exactCapy.OnAssigned(target);
            response = $"<color=#4ade80>✔</color> Роль <b>{exactCapy.Name}</b> выдана игроку <b>{target.Nickname}</b>.";
            return true;
        }

        // Поиск по префиксу
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
                response = $"<color=#4ade80>✔</color> Роль <b>{role.Name}</b> (ID: {role.Id}) выдана игроку <b>{target.Nickname}</b>.";
                return true;
            }
            else
            {
                var role = prefixMatchesCapy[0];
                role.OnAssigned(target);
                response = $"<color=#4ade80>✔</color> Роль <b>{role.Name}</b> выдана игроку <b>{target.Nickname}</b>.";
                return true;
            }
        }

        // Частичный поиск
        var containsMatchesExiled = allExiledRoles
            .Where(r => r.Name.ToLowerInvariant().Contains(query))
            .ToList();

        var containsMatchesCapy = allCapyRoles
            .Where(r => r.Name.ToLowerInvariant().Contains(query))
            .ToList();

        var allMatches = new List<string>();
        allMatches.AddRange(prefixMatchesExiled.Select(r => $"{r.Name} (ID: {r.Id})"));
        allMatches.AddRange(prefixMatchesCapy.Select(r => r.Name));

        if (allMatches.Count == 0)
        {
            allMatches.AddRange(containsMatchesExiled.Select(r => $"{r.Name} (ID: {r.Id})"));
            allMatches.AddRange(containsMatchesCapy.Select(r => r.Name));
        }

        if (allMatches.Count > 1)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<color=#facc15>⚠</color> По запросу '<b>{query}</b>' найдено {allMatches.Count} ролей:");
            foreach (var match in allMatches)
            {
                sb.AppendLine($"  <color=#ffd285>• {match}</color>");
            }
            sb.Append("Уточните название или введите <b>gcr</b>.");
            response = sb.ToString();
            return false;
        }

        response = $"<color=#ef4444>✖</color> Роль '<b>{query}</b>' не найдена.\nВведите <b>gcr</b> для списка всех доступных ролей.";
        return false;
    }

    private static string? TryGiveConceptRole(string query, Player target)
    {
        switch (query)
        {
            // === ХАКЕРЫ ===
            case "hack" or "hacker" or "hackers" or "h" or "хакер" or "хакеры" or "hacker_lead":
                NoRulesPlugin.Instance?.Hackers?.AddHacker(target);
                return $"<color=#4ade80>✔</color> Игроку <b>{target.Nickname}</b> выдана роль <color=#a78bfa><b>Хакер</b></color>.";

            case "hackg" or "guard" or "hg" or "hguard" or "hackerguard" or "hacker_guard" or "охранник" or "гвард" or "охранник_хакера":
                NoRulesPlugin.Instance?.Hackers?.AddGuard(target);
                return $"<color=#4ade80>✔</color> Игроку <b>{target.Nickname}</b> выдана роль <color=#ef4444><b>Охранник Хакера</b></color>.";

            // === СО2 ===
            case "co2cap" or "co2c" or "cap" or "co2_captain" or "co2captain" or "капитан_со2" or "со2_капитан" or "капитан":
                NoRulesPlugin.Instance?.Co2?.AddCaptain(target);
                return $"<color=#4ade80>✔</color> Игроку <b>{target.Nickname}</b> выдана роль <color=#14b1e0><b>Капитан СО₂</b></color>.";

            case "co2spec" or "co2s" or "spec" or "co2_spec" or "co2_specialist" or "спец" or "специалист" or "со2_специалист":
                NoRulesPlugin.Instance?.Co2?.AddSpecialist(target);
                return $"<color=#4ade80>✔</color> Игроку <b>{target.Nickname}</b> выдана роль <color=#14b1e0><b>Специалист СО₂</b></color>.";

            case "co2" or "co2cadet" or "co2_cadet" or "со2" or "кадет" or "co2squad" or "co2_operative":
                NoRulesPlugin.Instance?.Co2?.AddCadet(target);
                return $"<color=#4ade80>✔</color> Игроку <b>{target.Nickname}</b> выдана роль <color=#14b1e0><b>Оперативник СО₂</b></color>.";

            default:
                return null;
        }
    }

    private static string BuildRolesList()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<color=#ffd285><b>⚡ КАСТОМНЫЕ РОЛИ // МЕНЮ ВЫДАЧИ:</b></color>\n");

        sb.AppendLine("<color=#a78bfa><b>💻 Группировка «Хакеры»:</b></color>");
        sb.AppendLine("  • <color=#a78bfa><b>hack</b></color> <color=#94a3b8>(hacker, h)</color> — Хакер (Взлом терминалов HCZ, запуск OMEGA)");
        sb.AppendLine("  • <color=#ef4444><b>hackg</b></color> <color=#94a3b8>(guard, hg)</color> — Охранник Хакера (Тяжёлая броня, дробовик, гранаты)\n");

        sb.AppendLine("<color=#14b1e0><b>☣️ Спецотряд «СО₂»:</b></color>");
        sb.AppendLine("  • <color=#14b1e0><b>co2cap</b></color> <color=#94a3b8>(cap)</color> — Капитан СО₂ (Particle Disruptor + Карта)");
        sb.AppendLine("  • <color=#14b1e0><b>co2spec</b></color> <color=#94a3b8>(spec)</color> — Специалист СО₂ (E-11 + Карта МОГ + Медкит)");
        sb.AppendLine("  • <color=#14b1e0><b>co2</b></color> <color=#94a3b8>(cadet)</color> — Оперативник / Кадет СО₂\n");

        var capyRoles = CustomRolesManager.Roles.ToList();
        var exiledRoles = CustomRole.Registered.ToList();

        if (capyRoles.Count > 0 || exiledRoles.Count > 0)
        {
            sb.AppendLine("<color=#a3e635><b>🧩 Дополнительные роли:</b></color>");
            foreach (var r in capyRoles)
                sb.AppendLine($"  • <color=#ffd285><b>{r.Name}</b></color> <color=#94a3b8>(база: {r.BaseRole})</color>");
            foreach (var r in exiledRoles)
                sb.AppendLine($"  • <color=#ffd285><b>{r.Name}</b></color> <color=#94a3b8>(ID: {r.Id}, база: {r.Role})</color>");
            sb.AppendLine();
        }

        sb.Append("<color=#64748b>Быстрый ввод: <b>gcr hack [id]</b> | <b>gcr hackg [id]</b> | <b>gcr co2cap [id]</b></color>");
        return sb.ToString();
    }
}

