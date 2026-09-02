using Warudo.Core.Attributes;
using Warudo.Core.Plugins;
using Warudo.Plugins.McpBridge.Nodes;

namespace Warudo.Plugins.McpBridge {

    /// <summary>
    /// Plugin entry point used when packaging with the Warudo Mod SDK.
    /// Playground users can keep all source files together and Warudo will
    /// discover the declared asset and node types automatically.
    /// </summary>
    [PluginType(
        Id = "com.hasukatsu.warudo.outfitswitcher",
        Name = "Outfit Switcher",
        Description = "Outfit, hair and detachable accessory management with glow transitions.",
        Author = "KhoaDayy / Hasukatsu",
        Version = "2.0.0",
        AssetTypes = new[] { typeof(OutfitSwitcherAsset) },
        NodeTypes = new[] { typeof(OutfitSwitchNode), typeof(GlowOutfitNode) }
    )]
    public class OutfitSwitcherPlugin : Plugin {
    }
}
