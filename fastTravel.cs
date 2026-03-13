using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// Core Oxygen API namespaces required for plugin development
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

namespace FastTravelSystem
{
    #region Data Models

    // Represents a single fast-travel destination
    public class OutpostNode
    {
        public string DisplayName { get; set; }
        public string CommandAlias { get; set; }
        public double[] CenterZone { get; set; } = new double[2];
        public double[] ArrivalPoint { get; set; } = new double[3];
    }

    // Tracks the last time a player attempted to use fast travel
    public class PlayerTravelState 
    { 
        public DateTime LastTransferAttempt { get; set; } 
    }

    #endregion

    #region Configuration

    // Plugin configuration mapped automatically to a JSON file
    public class FastTravelConfig
    {
        public int TransferCooldownMinutes { get; set; } = 60;
        public double RatePerMeter { get; set; } = 0.5;
        public int TeleportDelaySeconds { get; set; } = 5;

        public List<OutpostNode> Outposts { get; set; } = new List<OutpostNode>
        {
            new OutpostNode { DisplayName = "A0 Trader", CommandAlias = "a0", CenterZone = new[]{-621973.438, -557260.438}, ArrivalPoint = new[]{-621973.438, -557260.438, 50.0} },
            new OutpostNode { DisplayName = "B4 Trader", CommandAlias = "b4", CenterZone = new[]{571278.250, -224427.219}, ArrivalPoint = new[]{571278.250, -224427.219, 50.0} },
            new OutpostNode { DisplayName = "C2 Trader", CommandAlias = "c2", CenterZone = new[]{-153247.156, 289822.219}, ArrivalPoint = new[]{-153247.156, 289822.219, 50.0} },
            new OutpostNode { DisplayName = "Z3 Trader", CommandAlias = "z3", CenterZone = new[]{24006.010, -676488.250}, ArrivalPoint = new[]{24006.010, -676488.250, 50.0} }
        };
    }

    #endregion

    [Info("FastTravel System", "Standalone", "1.2.0")]
    [Description("Example plugin demonstrating teleportation, economy integration, and async tasks.")]
    public class FastTravelPlugin : OxygenPlugin
    {
        private FastTravelConfig _cfg;
        private Dictionary<string, PlayerTravelState> _travelHistory;

        #region Lifecycle

        public override void OnLoad()
        {
            // Load existing config or create a new one
            _cfg = LoadConfig<FastTravelConfig>() ?? new FastTravelConfig();
            SaveConfig(_cfg);
            
            // Load persistent player data
            _travelHistory = LoadData<Dictionary<string, PlayerTravelState>>("FastTravel_Data") ?? new Dictionary<string, PlayerTravelState>();
        }

        public override void OnUnload() 
        {
            // Save data when the server shuts down or the plugin is reloaded
            SaveData("FastTravel_Data", _travelHistory);
        }

        #endregion

        #region Commands

        [Command("travel")]
        private void ProcessTravelCmd(PlayerBase player, string[] args)
        {
            // Show the travel menu if no destination is provided
            if (args.Length == 0)
            {
                ShowDirectory(player);
                return;
            }

            ExecuteTransferSequence(player, args[0].ToLower());
        }

        #endregion

        #region Core Logic & Helpers

        // Calculates the dynamic cost based on 2D distance
        private int CalculateFare(double startX, double startY, OutpostNode to)
        {
            double distMeters = Math.Sqrt(Math.Pow(to.CenterZone[0] - startX, 2) + Math.Pow(to.CenterZone[1] - startY, 2)) / 100.0;
            return (int)(distMeters * _cfg.RatePerMeter);
        }

        // Builds and displays the travel directory to the player
        private void ShowDirectory(PlayerBase player)
        {
            string msg = $"\n=== FAST TRAVEL ===\nBalance: ${player.Money:N0}\n\n";
            
            foreach (var node in _cfg.Outposts)
            {
                msg += $"[{node.CommandAlias}] {node.DisplayName} -> ${CalculateFare(player.Location.X, player.Location.Y, node):N0}\n";
            }
            msg += "\nUsage: /travel <code>";
            
            player.Reply(msg, Color.Blue);
        }

        // Async method to handle the teleportation sequence with delays
        private async void ExecuteTransferSequence(PlayerBase player, string targetAlias)
        {
            var destination = _cfg.Outposts.FirstOrDefault(o => o.CommandAlias == targetAlias);
            if (destination == null)
            {
                player.Reply("Invalid destination.", Color.Red);
                return;
            }

            // Ensure the player has a state record
            if (!_travelHistory.TryGetValue(player.SteamId, out var state))
            {
                state = new PlayerTravelState();
                _travelHistory[player.SteamId] = state;
            }

            // Verify cooldown
            var timeSinceLast = DateTime.Now - state.LastTransferAttempt;
            if (timeSinceLast.TotalMinutes < _cfg.TransferCooldownMinutes)
            {
                player.Reply($"Cooldown active! Remaining: {(int)(_cfg.TransferCooldownMinutes - timeSinceLast.TotalMinutes)} min.", Color.Orange);
                return;
            }

            // Verify funds
            int fare = CalculateFare(player.Location.X, player.Location.Y, destination);
            if (player.Money < fare)
            {
                player.Reply($"Insufficient funds. Required: ${fare:N0}", Color.Red);
                return;
            }

            // Notify player and begin the delay sequence
            player.Reply($"Preparing to travel to '{destination.DisplayName}' in {_cfg.TeleportDelaySeconds} seconds. Do not move!", Color.Yellow);

            // Wait for the delay and verify the player remained still
            bool success = await WaitForTeleport(player);

            // Execute transaction if the movement check passed
            if (success)
            {
                player.ProcessCommand($"Teleport {destination.ArrivalPoint[0]:F0} {destination.ArrivalPoint[1]:F0} {destination.ArrivalPoint[2]:F0}");
                player.ProcessCommand($"ChangeCurrencyBalance Normal -{fare}");
                
                state.LastTransferAttempt = DateTime.Now;
                SaveData("FastTravel_Data", _travelHistory);
                
                player.Reply($"Fast travel successful! Deducted: ${fare:N0}.", Color.Green);
            }
        }

        // Monitors player position to ensure they do not move during the teleport delay
        private async Task<bool> WaitForTeleport(PlayerBase player)
        {
            var startPos = player.Location;
            // Check position every 500ms
            int totalCheckIntervals = _cfg.TeleportDelaySeconds * 2; 

            for (int i = 0; i < totalCheckIntervals; i++)
            {
                await Task.Delay(500);

                double distance = Math.Sqrt(Math.Pow(player.Location.X - startPos.X, 2) + Math.Pow(player.Location.Y - startPos.Y, 2));
                
                // Cancel if the player moves more than 50 units (0.5 meters)
                if (distance > 50.0) 
                {
                    player.Reply("Fast travel cancelled! You moved.", Color.Red);
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}