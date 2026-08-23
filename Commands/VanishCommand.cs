using System;
using System.Collections.Generic;
using Capy.Engine.Hints;
using Capy.Engine.Hints.Extensions;
using CommandSystem;
using CustomPlayerEffects;
using Exiled.API.Enums;
using Exiled.API.Features;

namespace Capy.NoRules.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class VanishCommand : ICommand
{
    public string Command => "vanish";
    public string[] Aliases => new[] { "v", "invis" };
    public string Description => "Включить / выключить режим полной невидимости (для администрации).";

    private static readonly HashSet<int> VanishedPlayers = new();

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

        if (VanishedPlayers.Contains(target.Id))
        {
            // Выключаем Vanish
            VanishedPlayers.Remove(target.Id);
            target.DisableEffect<Invisible>();
            target.IsGodModeEnabled = false;
            target.IsBypassModeEnabled = false;

            target.ShowZoneHint(HintZone.TopCenter, "<color=#ff4444><b>👻 [VANISH] Режим невидимости ВЫКЛЮЧЕН</b></color>", 3.0f, "vanish_status", 22);
            response = $"<color=yellow>[VANISH]</color> Невидимость для игрока <b>{target.Nickname}</b> выключена.";
            return true;
        }
        else
        {
            // Включаем Vanish
            VanishedPlayers.Add(target.Id);
            target.EnableEffect<Invisible>(999999f, false);
            target.IsGodModeEnabled = true;
            target.IsBypassModeEnabled = true;

            target.ShowZoneHint(HintZone.TopCenter, "<color=#38bdf8><b>👻 [VANISH] Режим невидимости ВКЛЮЧЕН (GodMode + Bypass)</b></color>", 3.0f, "vanish_status", 22);
            response = $"<color=green>[VANISH]</color> Игрок <b>{target.Nickname}</b> стал полностью невидимым (GodMode + Bypass).";
            return true;
        }
    }

    public static bool IsVanished(Player player) => player != null && VanishedPlayers.Contains(player.Id);
}
