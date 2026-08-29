using Capy.NoRules.Concepts.Co2;
using CommandSystem;
using Exiled.API.Features;

namespace Capy.NoRules.Commands;

/// <summary>
/// .co2group — принудительный вызов отряда СО2 из спектаторов (админ).
/// </summary>
[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class Co2GroupCommand : ICommand
{
    public string Command => "co2group";
    public string[] Aliases => new[] { "co2" };
    public string Description => "Принудительно вызвать Отряд СО2 из спектаторов";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        var concept = NoRulesPlugin.Instance?.Co2;
        if (concept == null)
        {
            response = "Концепт CO2 выключен.";
            return false;
        }

        response = concept.ForceSpawnFromSpectators();
        return true;
    }
}

