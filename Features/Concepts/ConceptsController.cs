using Exiled.API.Features;

namespace Capy.NoRules.Features.Concepts;

/// <summary>
/// Глобальный контроллер концептов: в один момент времени активен только один
/// фракционный сценарий (CO2, Хакеры и т.д.). Сбрасывается на ожидании игроков.
/// </summary>
public static class ConceptsController
{
    private static bool _activated;
    private static string _activeName = string.Empty;

    /// <summary>Активен ли какой-либо концепт прямо сейчас.</summary>
    public static bool IsActivated => _activated;

    /// <summary>Имя активного концепта.</summary>
    public static string ActiveName => _activeName;

    public static void Activate(string conceptName)
    {
        _activated = true;
        _activeName = conceptName;
        Log.Debug($"[Concepts] Активирован концепт '{conceptName}'.");
    }

    public static void Disable()
    {
        if (!_activated) return;

        Log.Debug($"[Concepts] Концепт '{_activeName}' деактивирован.");
        _activated = false;
        _activeName = string.Empty;
    }
}
