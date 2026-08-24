using System;
using System.Globalization;
using System.Linq;
using Capy.NoRules.Features;
using CommandSystem;
using Exiled.API.Features;

namespace Capy.NoRules.Commands;

/// <summary>
/// Резолв аргумента в UserId: полный userId / номер игрока / ник онлайн-игрока.
/// Оффлайн-игроков можно указывать только полным userId (с '@').
/// </summary>
public static class XpTargetResolver
{
    public static bool TryResolve(string raw, out string userId, out string error)
    {
        userId = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Укажите игрока: <userId | номер | ник>";
            return false;
        }

        // Полный UserId
        if (raw.Contains('@'))
        {
            userId = raw;
            return true;
        }

        // По номеру игрока
        if (int.TryParse(raw, out int playerId))
        {
            Player? byId = Player.List.FirstOrDefault(p => p.Id == playerId);
            if (byId != null)
            {
                userId = byId.UserId;
                return true;
            }

            error = $"Игрок с номером {playerId} не найден на сервере.";
            return false;
        }

        // По нику (точное совпадение, затем частичное)
        Player? exact = Player.List.FirstOrDefault(p =>
            p.Nickname.Equals(raw, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            userId = exact.UserId;
            return true;
        }

        Player? partial = Player.List.FirstOrDefault(p =>
            p.Nickname.IndexOf(raw, StringComparison.OrdinalIgnoreCase) >= 0);
        if (partial != null)
        {
            userId = partial.UserId;
            return true;
        }

        error = $"'{raw}' — игрок не найден. Для оффлайн-игрока укажите полный userId (например: givexp 76561198...@steam 100).";
        return false;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class GiveXpCommand : ICommand
{
    public string Command => "givexp";
    public string[] Aliases => new[] { "gxp" };
    public string Description => "Выдать игроку определённое количество опыта";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (arguments.Count < 2)
        {
            response = "Использование: givexp <userId | номер | ник> <amount> (пример: givexp DOXA 100)";
            return false;
        }

        var feature = NoRulesPlugin.Instance?.PlayerXp;
        if (feature == null || !feature.IsEnabled())
        {
            response = "Система опыта выключена.";
            return false;
        }

        if (!XpTargetResolver.TryResolve(arguments.At(0), out string userId, out string resolveError))
        {
            response = resolveError;
            return false;
        }

        if (!float.TryParse(arguments.At(1), out float amount) || amount == 0f)
        {
            response = $"'{arguments.At(1)}' не является ненулевым числом!";
            return false;
        }

        // Начисляем напрямую в БД (без делителя/тега — админ задаёт точное значение)
        feature.SetRawXp(userId, string.Empty, feature.GetXp(userId) + amount);

        var level = feature.GetLevelFor(userId);
        response = $"Выдано {userId} +{amount} опыта. Итого: {feature.GetXp(userId):F1} XP{(level != null ? $", титул: {level.Text}" : "")}.";
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class SetXpCommand : ICommand
{
    public string Command => "setplayerxp";
    public string[] Aliases => new[] { "spxp" };
    public string Description => "Задать игроку определённое количество опыта";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (arguments.Count < 2)
        {
            response = "Использование: setplayerxp <userId | номер | ник> <amount>";
            return false;
        }

        var feature = NoRulesPlugin.Instance?.PlayerXp;
        if (feature == null || !feature.IsEnabled())
        {
            response = "Система опыта выключена.";
            return false;
        }

        if (!XpTargetResolver.TryResolve(arguments.At(0), out string userId, out string resolveError))
        {
            response = resolveError;
            return false;
        }

        if (!float.TryParse(arguments.At(1), out float amount) || amount < 0f)
        {
            response = $"'{arguments.At(1)}' не является корректным числом (>= 0)!";
            return false;
        }

        feature.SetRawXp(userId, string.Empty, amount);
        response = $"Установлено {userId} {amount} опыта.";
        return true;
    }
}

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class CheckXpCommand : ICommand
{
    public string Command => "checkxp";
    public string[] Aliases => new[] { "cxp" };
    public string Description => "Узнать количество опыта у определённого игрока";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (arguments.Count < 1)
        {
            response = "Использование: checkxp <userId | номер | ник>";
            return false;
        }

        var feature = NoRulesPlugin.Instance?.PlayerXp;
        if (feature == null || !feature.IsEnabled())
        {
            response = "Система опыта выключена.";
            return false;
        }

        if (!XpTargetResolver.TryResolve(arguments.At(0), out string userId, out string resolveError))
        {
            response = resolveError;
            return false;
        }

        float xp = feature.GetXp(userId);
        var level = feature.GetLevelFor(userId);
        response = $"У игрока {userId} {xp:F1} опыта{(level != null ? $", титул: {level.Text}" : "")}.";
        return true;
    }
}
