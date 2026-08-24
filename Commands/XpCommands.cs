using System;
using Capy.NoRules.Features;
using CommandSystem;
using Exiled.API.Features;

namespace Capy.NoRules.Commands;

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
            response = "Использование: givexp <userId> <amount>";
            return false;
        }

        string userId = arguments.At(0);
        if (!float.TryParse(arguments.At(1), out float amount))
        {
            response = $"'{arguments.At(1)}' не является числом!";
            return false;
        }

        var db = NoRulesPlugin.Instance?.PlayerXp?.Database;
        if (db == null)
        {
            response = "Система опыта выключена.";
            return false;
        }

        db.EnsurePlayer(userId, db.GetNickname(userId) ?? string.Empty);
        db.GiveXp(userId, amount);

        var level = db.GetLevel(userId);
        response = $"Выдано {userId} {amount} опыта. Уровень: {(level != null ? $"{level.Text} ({db.GetXp(userId):F1} XP)" : "—")}";
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
            response = "Использование: setplayerxp <userId> <amount>";
            return false;
        }

        string userId = arguments.At(0);
        if (!float.TryParse(arguments.At(1), out float amount))
        {
            response = $"'{arguments.At(1)}' не является числом!";
            return false;
        }

        var db = NoRulesPlugin.Instance?.PlayerXp?.Database;
        if (db == null)
        {
            response = "Система опыта выключена.";
            return false;
        }

        db.EnsurePlayer(userId, db.GetNickname(userId) ?? string.Empty);
        db.SetXp(userId, amount);

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
            response = "Использование: checkxp <userId>";
            return false;
        }

        string userId = arguments.At(0);
        var db = NoRulesPlugin.Instance?.PlayerXp?.Database;
        if (db == null)
        {
            response = "Система опыта выключена.";
            return false;
        }

        if (!db.Contains(userId))
        {
            response = $"Игрок {userId} не найден в базе опыта.";
            return false;
        }

        float xp = db.GetXp(userId);
        var level = db.GetLevel(userId);
        response = $"У игрока {userId} {xp:F1} опыта{(level != null ? $", уровень: {level.Text}" : "")}.";
        return true;
    }
}
