using System;
using CommandSystem;

namespace Capy.NoRules.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class ResToggleCommand : ICommand
{
    public string Command => "restoggle";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "Включить / отключить команду .res для игроков.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (NoRulesPlugin.Instance?.DotResKill == null)
        {
            response = "Модуль .res не активен.";
            return false;
        }

        NoRulesPlugin.Instance.DotResKill.ResAllowed = !NoRulesPlugin.Instance.DotResKill.ResAllowed;
        string status = NoRulesPlugin.Instance.DotResKill.ResAllowed ? "<color=green>ВКЛЮЧЕНА</color>" : "<color=red>ОТКЛЮЧЕНА</color>";
        response = $"Команда .res теперь {status}.";
        return true;
    }
}
