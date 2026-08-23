using System;
using CommandSystem;
using Exiled.API.Features;

namespace Capy.NoRules.Commands;

[CommandHandler(typeof(ClientCommandHandler))]
public sealed class ResCommand : ICommand
{
    public string Command => "res";
    public string[] Aliases => new[] { "respawn" };
    public string Description => "Возродиться за Класс D или Учёного в первые минуты раунда.";

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
            response = "Модуль .res не загружен.";
            return false;
        }

        return NoRulesPlugin.Instance.DotResKill.ExecuteRes(player, out response);
    }
}
