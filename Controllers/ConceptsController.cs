using Exiled.API.Features;

namespace Capy.NoRules.Controllers;

/// <summary>
/// Глобальный контроллер активных концептов.
/// Позволяет отслеживать активность концептов (CO2, Хакеры, SCP-008, AirDrop и т.д.)
/// и предотвращать конфликты между одновременными глобальными событиями.
/// </summary>
public static class ConceptsController
{
    private static bool _activated;
    private static string _activeConceptName = string.Empty;

    public static bool IsActivated => _activated;
    public static string ActiveConceptName => _activeConceptName;

    public static void Activate(string conceptName = "")
    {
        _activated = true;
        _activeConceptName = conceptName;
    }

    public static void Disable()
    {
        _activated = false;
        _activeConceptName = string.Empty;
    }

    public static void Init()
    {
        Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
        Exiled.Events.Handlers.Server.RestartingRound += OnRestartingRound;
    }

    public static void Unload()
    {
        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
        Exiled.Events.Handlers.Server.RestartingRound -= OnRestartingRound;
    }

    private static void OnWaitingForPlayers()
    {
        _activated = false;
        _activeConceptName = string.Empty;
    }

    private static void OnRestartingRound()
    {
        _activated = false;
        _activeConceptName = string.Empty;
    }
}
