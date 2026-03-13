using System;
using System.Collections.Generic;
using System.Linq;
// Core Oxygen API namespaces
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

namespace ItemRestrictionSystem
{
    // Configuration class for defining blocked items and bypass permissions
    public class RestrictionConfig
    {
        public bool Enabled { get; set; } = true;
        
        // List of item class names to block (e.g., "BP_Weapon_AK47")
        public List<string> ForbiddenItems { get; set; } = new List<string> 
        { 
            "Weapon_M1887_С", 
            "Weapon_RPG7_C" 
        };

        public string BypassPermission { get; set; } = "items.bypass";
        public string BlockMessage { get; set; } = "You are not allowed to use this item!";
    }

    [Info("Item Restrictions", "Standalone", "1.0.0")]
    [Description("Example plugin that prevents players from taking specific items into their hands.")]
    public class ItemRestrictionPlugin : OxygenPlugin
    {
        private RestrictionConfig _cfg;

        // Triggered when the plugin is loaded by the framework
        public override void OnLoad()
        {
            // Load the JSON config or create a default one
            _cfg = LoadConfig<RestrictionConfig>() ?? new RestrictionConfig();
            SaveConfig(_cfg);
        }

        // Hook triggered when a player attempts to equip an item into their hands
        // Returning FALSE cancels the action. Returning TRUE allows it.
        public override bool OnPlayerTakeItemInHands(PlayerBase player, string itemName)
        {
            Console.WriteLine(itemName);

            // 1. Check if the restriction system is active
            if (!_cfg.Enabled) return true;

            // 2. Allow admins or players with the bypass permission to use anything
            if (player.HasPermission(_cfg.BypassPermission)) return true;

            // 3. Check if the item being equipped is in the forbidden list (Case-Insensitive)
            bool isForbidden = _cfg.ForbiddenItems.Any(i => i.Equals(itemName, StringComparison.OrdinalIgnoreCase));

            if (isForbidden)
            {
                // Send a notification to the player's screen
                player.ProcessCommand($"SendNotification 2 {player.DatabaseId} \"{_cfg.BlockMessage}\"");
                
                // Log the attempt to the server console
                Console.WriteLine($"[Item Restrictions] Blocked {player.Name} from using {itemName}");

                // REJECT the action
                return false;
            }

            // ALLOW the action for all other items
            return true;
        }
    }
}