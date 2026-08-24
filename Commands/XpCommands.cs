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

        var feature = NoRulesPlugin.Instance?.PlayerXp;
        if (feature == null || !feature.IsEnabled())
        {
            response = "Система опыта выключена.";
            return false;
        }

        feature.SetRawXp(userId, string.Empty, feature.GetXp(userId) + amount);

        var level = feature.GetLevelFor(userId);
        response = $"Выдано {userId} {amount} опыта. Итого: {feature.GetXp(userId):F1} XP{(level != null ? $", уровень: {level.Text}" : "")}.";
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

        var feature = NoRulesPlugin.Instance?.PlayerXp;
        if (feature == null || !feature.IsEnabled())
        {
            response = "Система опыта выключена.";
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
            response = "Использование: checkxp <userId>";
            return false;
        }

        string userId = arguments.At(0);
        var feature = NoRulesPlugin.Instance?.PlayerXp;
        if (feature == null || !feature.IsEnabled())
        {
            response = "Система опыта выключена.";
            return false;
        }

        if (!feature.HasRecord(userId))
        {
            response = $"Игрок {userId} не найден в базе опыта.";
            return false;
        }

        float xp = feature.GetXp(userId);
        var level = feature.GetLevelFor(userId);
        response = $"У игрока {userId} {xp:F1} опыта{(level != null ? $", уровень: {level.Text}" : "")}.";
        return true;
    }
}
