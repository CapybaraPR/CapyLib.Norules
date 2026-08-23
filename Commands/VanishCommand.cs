using System;
using CommandSystem;
using Exiled.API.Features;

namespace Capy.NoRules.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class VanishCommand : ICommand
{
    public string Command => "vanish";
    public string[] Aliases => new[] { "v", "invis" };
    public string Description => "Включить / выключить режим скрытности администратора (Vanish).";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        Player? target = null;
        if (arguments.Count >= 1)
        {
            string playerQuery = arguments.At(0);
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
                response = "<color=red>[ОШИБКА]</color> Укажите ID игрока или используйте команду в игре.";
                return false;
            }
        }

        if (NoRulesPlugin.Instance?.Vanish == null)
        {
            response = "<color=red>[ОШИБКА]</color> Модуль Vanish не загружен.";
            return false;
        }

        return NoRulesPlugin.Instance.Vanish.Toggle(target, out response);
    }
}
