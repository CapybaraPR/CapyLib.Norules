using Capy.Engine.Hints.Extensions;
using Capy.NoRules.Config;
using Exiled.API.Features;

namespace Capy.NoRules.Addons;

/// <summary>
/// Система автоматического включения Friendly Fire при завершении раунда.
/// </summary>
public sealed class FriendlyFireFeature
{
    private readonly NoRulesFriendlyFireConfig _config;

    public FriendlyFireFeature(NoRulesFriendlyFireConfig config)
    {
        _config = config;
    }

    public void OnRoundEnded()
    {
        if (!_config.EnableAtRoundEnd) return;

        Server.FriendlyFire = true;

        if (!string.IsNullOrWhiteSpace(_config.RoundEndMessage))
        {
            ShowHintExtensions.ShowAnnouncementToAll(_config.RoundEndMessage, 8.0f, "ff_round_end");
        }
    }

    public void OnRoundStarted()
    {
        Server.FriendlyFire = false;
    }
}

