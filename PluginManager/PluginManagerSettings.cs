using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;

namespace PluginManager
{
    public class PluginManagerSettings : ISettings
    {
        public ToggleNode Enable { get; set; } = new ToggleNode(false);

    }
}
