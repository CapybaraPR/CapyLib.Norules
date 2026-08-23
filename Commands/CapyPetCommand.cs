using System;
using System.Globalization;
using System.Linq;
using CommandSystem;
using Exiled.API.Features;
using UnityEngine;

namespace Capy.NoRules.Commands;

/// <summary>
/// Команда управления 3D-капибарами и летающими питомцами.
/// Доступна владельцу SteamID и администраторам сервера.
/// </summary>
[CommandHandler(typeof(ClientCommandHandler))]
[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class CapyPetCommand : ICommand
{
    public string Command => "capy";
    public string[] Aliases => new[] { "capybara", "pet", "petcapy" };
    public string Description => "Управление 3D-капибарами (спавн статичной перед собой, выдача питомца игрокам).";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        Player? player = Player.Get(sender);
        if (player == null)
        {
            response = "<color=red>[ОШИБКА]</color> Команда доступна только игрокам на сервере.";
            return false;
        }

        var petFeature = NoRulesPlugin.Instance?.CapybaraPet;
        if (petFeature == null)
        {
            response = "<color=red>[ОШИБКА]</color> Модуль CapybaraPet не загружен.";
            return false;
        }

        if (!petFeature.HasAccess(player))
        {
            response = "<color=red>[ДОСТУП ЗАПРЕЩЁН]</color> У вас нет прав для управления капибарами.";
            return false;
        }

        if (arguments.Count == 0)
        {
            response = "\n<color=#ffa94e><b>🦫 === [ УПРАВЛЕНИЕ КАПИБАРАМИ ] ===</b></color>\n" +
                       "<color=#a3e635>• .capy spawn [масштаб]</color> -- Заспавнить статичную 3D-капибару перед собой (для осмотра)\n" +
                       "<color=#a3e635>• .capy toggle</color> -- Включить / выключить летающего питомца у себя\n" +
                       "<color=#a3e635>• .capy give <ID/Ник></color> -- Выдать летающего питомца игроку на раунд\n" +
                       "<color=#a3e635>• .capy remove <ID/Ник></color> -- Убрать питомца у игрока\n" +
                       "<color=#a3e635>• .capy clear</color> -- Удалить все заспавненные статичные капибары";
            return true;
        }

        string subCmd = arguments.At(0).ToLowerInvariant();

        switch (subCmd)
        {
            case "spawn":
            case "here":
            case "freeze":
            case "static":
            {
                float scale = 0.8f;
                if (arguments.Count > 1 && float.TryParse(arguments.At(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    scale = Mathf.Clamp(parsed, 0.1f, 5.0f);
                }

                return petFeature.SpawnStaticCapybara(player, scale, out response);
            }

            case "toggle":
            {
                return petFeature.TogglePet(player, out response);
            }

            case "clear":
            {
                int count = petFeature.ClearAllStaticCapybaras();
                response = $"<color=yellow>[КАПИБАРА]</color> Удалено статичных капибар с карты: <b>{count}</b>.";
                return true;
            }

            case "give":
            {
                if (arguments.Count < 2)
                {
                    response = "<color=red>[ОШИБКА]</color> Использование: <b>.capy give <ID/Ник/me></b>";
                    return false;
                }

                string targetStr = arguments.At(1);
                Player? target = targetStr.Equals("me", StringComparison.OrdinalIgnoreCase)
                    ? player
                    : Player.Get(targetStr);

                if (target == null || !target.IsConnected)
                {
                    response = $"<color=red>[ОШИБКА]</color> Игрок '{targetStr}' не найден.";
                    return false;
                }

                return petFeature.GivePet(target, out response);
            }

            case "remove":
            case "take":
            {
                if (arguments.Count < 2)
                {
                    response = "<color=red>[ОШИБКА]</color> Использование: <b>.capy remove <ID/Ник/me></b>";
                    return false;
                }

                string targetStr = arguments.At(1);
                Player? target = targetStr.Equals("me", StringComparison.OrdinalIgnoreCase)
                    ? player
                    : Player.Get(targetStr);

                if (target == null || !target.IsConnected)
                {
                    response = $"<color=red>[ОШИБКА]</color> Игрок '{targetStr}' не найден.";
                    return false;
                }

                return petFeature.RemovePet(target, out response);
            }

            default:
            {
                response = $"<color=red>[ОШИБКА]</color> Неизвестная подкоманда '{subCmd}'. Введите <b>.capy</b> для списка команд.";
                return false;
            }
        }
    }
}
