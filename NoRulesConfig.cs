using Capy.Core.API;

namespace Capy.NoRules;

public class NoRulesConfig : IModuleConfig
{
    public bool IsEnabled { get; set; } = true;
    public bool DebugMode { get; set; }
}
