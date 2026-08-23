using System;
using CommandSystem;
using Exiled.API.Features;

namespace Capy.NoRules.Commands;

[CommandHandler(typeof(ClientCommandHandler))]
public sealed class KillCommand : ICommand
{
    public string Command => "kill";
    public string[] Aliases => new[] { "killme", "suicide" };
    public string Description => "Совершить самоубийство (в игре).";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        Player player = Player.Get(sender);
        if (player == null)
        {
            response = "Эту команду могут вызывать только игроки в игре.";
            return false;
        }

        if (NoRulesPlugin.Instance?.DotResKill == null)
        {
            response = "Модуль .kill не загружен.";
            return false;
        }

        return NoRulesPlugin.Instance.DotResKill.ExecuteKill(player, out response);
    }
}
