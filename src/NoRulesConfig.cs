using Exiled.API.Interfaces;

namespace Capy.NoRules;

public class NoRulesConfig : IConfig
{
    public bool IsEnabled { get; set; } = true;
    public bool Debug { get; set; }
}
