using Exiled.API.Features;
using Exiled.Events.EventArgs.Warhead;

namespace Capy.NoRules.Controllers;

/// <summary>
/// Контроллер состояния Альфа-боеголовки.
/// Позволяет концептам и модулям блокировать/разрешать включение боеголовки.
/// </summary>
public static class AlphaController
{
    private static bool _locked;
    private static bool _lockedChange;

    public static bool IsLocked => _locked;

    public static void ChangeState(bool @new, bool always = false)
    {
        if (!@new && _lockedChange)
            return;

        _locked = @new;

        if (always)
            _lockedChange = true;
    }

    public static void DisableLock()
    {
        _lockedChange = false;
    }

    public static void Init()
    {
        Exiled.Events.Handlers.Warhead.Starting += OnWarheadStarting;
        Exiled.Events.Handlers.Server.WaitingForPlayers += OnWaitingForPlayers;
    }

    public static void Unload()
    {
        Exiled.Events.Handlers.Warhead.Starting -= OnWarheadStarting;
        Exiled.Events.Handlers.Server.WaitingForPlayers -= OnWaitingForPlayers;
    }

    private static void OnWarheadStarting(StartingEventArgs ev)
    {
        if (!_locked)
            return;

        ev.IsAllowed = false;
    }

    private static void OnWaitingForPlayers()
    {
        _locked = false;
        _lockedChange = false;
    }
}
