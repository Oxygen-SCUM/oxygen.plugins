using System;
using System.Collections.Generic;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

namespace SpawnSystem
{
    #region Configuration

    public class SpawnSettings
    {
        // Items to be automatically put into clothing slots (clothes, bags, etc.)
        public List<string> Equipment { get; set; } = new List<string>();
        
        // Items to be placed inside pockets or backpack (tools, consumables)
        public List<string> Inventory { get; set; } = new List<string>();
    }

    public class RespawnConfig
    {
        public bool Enabled { get; set; } = true;
        public string PrivilegedPermission { get; set; } = "spawn.privileged";
        public string Message { get; set; } = "Your starter items have been delivered.";

        // Default items for regular players
        public SpawnSettings StandardSet { get; set; } = new SpawnSettings
        {
            Equipment = new List<string> { "Bamboo_Hat_02", "Beijing_Shoes_04", "Tang_pants_04", "Tang_shirt_04" },
            Inventory = new List<string> { "Apple_2", "BeefRavioli", "CannedGoulash", "Emergency_Bandage_Big" }
        };

        // Improved items for privileged players
        public SpawnSettings PrivilegedSet { get; set; } = new SpawnSettings
        {
            Equipment = new List<string> { "Military_Boonie_Hat_07", "Camouflage_Jacket_01", "Hiking_Boots_03", "Open_Finger_Gloves_01", "Short_Trousers_01_05" },
            Inventory = new List<string> { "Weapon_BlackHawk_Crossbow", "2H_Baseball_Bat_With_Wire", "MRE_Stew", "Emergency_Bandage_Big" }
        };
    }

    #endregion

    [Info("Custom Respawn Items", "Standalone", "1.1.0")]
    [Description("Gives specific item sets to standard and privileged players upon respawn.")]
    public class CustomRespawnPlugin : OxygenPlugin
    {
        private RespawnConfig _cfg;

        #region Initialization

        public override void OnLoad()
        {
            // Load configuration or create a new one from defaults
            _cfg = LoadConfig<RespawnConfig>() ?? new RespawnConfig();
            SaveConfig(_cfg);
        }

        #endregion

        #region Hook: OnPlayerRespawned

        public override void OnPlayerRespawned(PlayerBase player)
        {
            if (!_cfg.Enabled) return;

            // 1. Clear default prison gear to make space for custom items
            player.Inventory.Clear();

            player.ProcessCommand("EquipParachute"); // if air spawn

            // 2. Determine which set to use based on player's permission
            bool isPrivileged = player.HasPermission(_cfg.PrivilegedPermission);
            SpawnSettings selectedSet = isPrivileged ? _cfg.PrivilegedSet : _cfg.StandardSet;

            // 3. First, equip clothing and backpacks (important for inventory space)
            foreach (var item in selectedSet.Equipment)
            {
                player.EquipItem(item);
            }

            // 4. Then, give tools and weapons into the equipped containers
            foreach (var item in selectedSet.Inventory)
            {
                player.GiveItem(item);
            }
        }

        #endregion
    }
}