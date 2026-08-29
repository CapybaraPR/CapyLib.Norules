using System;
using CommandSystem;
using Exiled.API.Features;

namespace Capy.NoRules.Commands;

/// <summary>
/// Команда свободного наблюдателя. Доступна ВСЕМ игрокам через клиентскую консоль (~).
/// Вход разрешён только из роли Spectator.
/// </summary>
[CommandHandler(typeof(ClientCommandHandler))]
public sealed class VanishCommand : ICommand
{
    public string Command => "vanish";
    public string[] Aliases => new[] { "v", "freecam", "spec" };
    public string Description => "Включить / выключить режим свободного наблюдателя (Free Spectator).";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        Player? player = Player.Get(sender);
        if (player == null)
        {
            response = "<color=red>[ОШИБКА]</color> Эта команда доступна только игрокам на сервере.";
            return false;
        }

        if (NoRulesPlugin.Instance?.Vanish == null)
        {
            response = "<color=red>[ОШИБКА]</color> Модуль свободного наблюдателя не загружен.";
            return false;
        }

        return NoRulesPlugin.Instance.Vanish.Toggle(player, out response);
    }
}

