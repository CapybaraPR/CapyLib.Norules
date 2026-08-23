using System;
using CommandSystem;

namespace Capy.NoRules.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class KillToggleCommand : ICommand
{
    public string Command => "killtoggle";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "Включить / отключить команду .kill для игроков.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (NoRulesPlugin.Instance?.DotResKill == null)
        {
            response = "Модуль .kill не активен.";
            return false;
        }

        NoRulesPlugin.Instance.DotResKill.KillAllowed = !NoRulesPlugin.Instance.DotResKill.KillAllowed;
        string status = NoRulesPlugin.Instance.DotResKill.KillAllowed ? "<color=green>ВКЛЮЧЕНА</color>" : "<color=red>ОТКЛЮЧЕНА</color>";
        response = $"Команда .kill теперь {status}.";
        return true;
    }
}
