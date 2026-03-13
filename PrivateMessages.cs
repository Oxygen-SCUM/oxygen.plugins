using System;
using System.Collections.Generic;
using System.Linq;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

namespace PrivateMessagesSystem
{
    #region Configuration

    public class PMConfig
    {
        public bool UsePermission { get; set; } = false;
        public string AllowPermission { get; set; } = "pm.use";
        
        public bool UseCooldown { get; set; } = true;
        public int CooldownTimeSeconds { get; set; } = 3;
        
        public bool EnableLogging { get; set; } = true;
        public bool EnableHistory { get; set; } = true;
    }

    #endregion

    #region Data Models

    public class PmHistoryRecord
    {
        public string User1Id { get; set; }
        public string User2Id { get; set; }
        public List<string> Messages { get; set; } = new List<string>();
    }

    #endregion

    [Info("Private Messages", "Standalone", "1.0.0")]
    [Description("Allows users to send private messages and reply to each other.")]
    public class PrivateMessagesPlugin : OxygenPlugin
    {
        private PMConfig _cfg;
        
        // Tracks who last messaged who for the /r (reply) command: SteamId -> Target SteamId
        private Dictionary<string, string> _replyTargets = new Dictionary<string, string>();
        
        // Tracks cooldown expirations: SteamId -> Expiration Time
        private Dictionary<string, DateTime> _cooldowns = new Dictionary<string, DateTime>();
        
        // Stores the last 5 messages between two players
        private List<PmHistoryRecord> _pmHistory = new List<PmHistoryRecord>();

        #region Initialization

        public override void OnLoad()
        {
            _cfg = LoadConfig<PMConfig>() ?? new PMConfig();
            SaveConfig(_cfg);
        }

        // Clean up memory when a player leaves
        public override void OnPlayerDisconnected(PlayerBase player)
        {
            _replyTargets.Remove(player.SteamId);
            _cooldowns.Remove(player.SteamId);
        }

        #endregion

        #region Commands

        [Command("pm")]
        private void SendPrivateMessageCommand(PlayerBase player, string[] args)
        {
            if (_cfg.UsePermission && !player.HasPermission(_cfg.AllowPermission))
            {
                player.Reply("You don't have permission to use private messages.", Color.Red);
                return;
            }

            if (args.Length < 2)
            {
                player.Reply("Incorrect Syntax. Use: /pm <name> <message>", Color.Red);
                return;
            }

            string targetName = args[0];
            string message = string.Join(" ", args.Skip(1));

            PlayerBase target = FindPlayer(targetName);

            if (target == null)
            {
                player.Reply($"Player '{targetName}' is not online or cannot be found.", Color.Red);
                return;
            }

            if (target.SteamId == player.SteamId)
            {
                player.Reply("You cannot send messages to yourself.", Color.Red);
                return;
            }

            if (IsCooldownActive(player)) return;

            ProcessMessage(player, target, message);
        }

        [Command("r")]
        private void ReplyPrivateMessageCommand(PlayerBase player, string[] args)
        {
            if (_cfg.UsePermission && !player.HasPermission(_cfg.AllowPermission))
            {
                player.Reply("You don't have permission to use private messages.", Color.Red);
                return;
            }

            if (args.Length < 1)
            {
                player.Reply("Incorrect Syntax. Use: /r <message>", Color.Red);
                return;
            }

            if (!_replyTargets.TryGetValue(player.SteamId, out string targetSteamId))
            {
                player.Reply("You haven't messaged anyone or they haven't messaged you.", Color.Red);
                return;
            }

            PlayerBase target = FindPlayer(targetSteamId);

            if (target == null)
            {
                player.Reply("The last person you were talking to is no longer online.", Color.Red);
                return;
            }

            if (IsCooldownActive(player)) return;

            string message = string.Join(" ", args);
            ProcessMessage(player, target, message);
        }

        [Command("pmhistory")]
        private void PMHistoryCommand(PlayerBase player, string[] args)
        {
            if (!_cfg.EnableHistory)
            {
                player.Reply("Private message history is disabled on this server.", Color.Red);
                return;
            }

            if (args.Length != 1)
            {
                player.Reply("Incorrect Syntax. Use: /pmhistory <name>", Color.Red);
                return;
            }

            PlayerBase target = FindPlayer(args[0]);

            if (target == null)
            {
                player.Reply($"Player '{args[0]}' is not online.", Color.Red);
                return;
            }

            var history = GetHistoryRecord(player.SteamId, target.SteamId);

            if (history == null || history.Messages.Count == 0)
            {
                player.Reply($"You have no message history with {target.Name}.", Color.Yellow);
                return;
            }

            string fullHistory = $"\n=== Message History with {target.Name} ===\n";
            fullHistory += string.Join("\n", history.Messages);
            
            player.Reply(fullHistory, Color.Blue);
        }

        #endregion

        #region Core Logic

        private void ProcessMessage(PlayerBase sender, PlayerBase target, string message)
        {
            // 1. Link players for the /r command
            _replyTargets[sender.SteamId] = target.SteamId;
            _replyTargets[target.SteamId] = sender.SteamId;

            // 2. Send messages to both screens
            sender.Reply($"[PM to {target.Name}]: {message}", Color.Blue);
            target.Reply($"[PM from {sender.Name}]: {message}", Color.Blue);

            target.ProcessCommand($"SendNotification 1 {target.DatabaseId} \"[PM from {sender.Name}]: {message}\"");

            // 3. Log to console if enabled
            if (_cfg.EnableLogging)
            {
                Console.WriteLine($"[PM] {sender.Name} -> {target.Name}: {message}");
            }

            // 4. Save to history if enabled
            if (_cfg.EnableHistory)
            {
                AddMessageToHistory(sender.SteamId, target.SteamId, $"[{sender.Name}]: {message}");
            }
        }

        private bool IsCooldownActive(PlayerBase player)
        {
            if (!_cfg.UseCooldown) return false;

            if (_cooldowns.TryGetValue(player.SteamId, out DateTime expiryTime))
            {
                if (DateTime.UtcNow < expiryTime)
                {
                    double remaining = (expiryTime - DateTime.UtcNow).TotalSeconds;
                    player.Reply($"Please wait {remaining:F1} seconds before messaging again.", Color.Orange);
                    return true;
                }
            }

            // Update cooldown timer
            _cooldowns[player.SteamId] = DateTime.UtcNow.AddSeconds(_cfg.CooldownTimeSeconds);
            return false;
        }

        #endregion

        #region Helper Methods

        // Safely searches through all connected players
        private PlayerBase FindPlayer(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return null;

            // 1. Check exact SteamID
            var target = Server.AllPlayers.FirstOrDefault(p => p.SteamId == identifier);
            if (target != null) return target;

            // 2. Check exact Name (case-insensitive)
            target = Server.AllPlayers.FirstOrDefault(p => p.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            if (target != null) return target;

            // 3. Check partial Name (case-insensitive)
            target = Server.AllPlayers.FirstOrDefault(p => p.Name.IndexOf(identifier, StringComparison.OrdinalIgnoreCase) >= 0);
            return target;
        }

        private PmHistoryRecord GetHistoryRecord(string user1, string user2)
        {
            // Find the shared conversation history regardless of who sent the first message
            return _pmHistory.FirstOrDefault(x => 
                (x.User1Id == user1 && x.User2Id == user2) || 
                (x.User1Id == user2 && x.User2Id == user1));
        }

        private void AddMessageToHistory(string senderId, string targetId, string formattedMessage)
        {
            var record = GetHistoryRecord(senderId, targetId);

            if (record == null)
            {
                record = new PmHistoryRecord
                {
                    User1Id = senderId,
                    User2Id = targetId
                };
                _pmHistory.Add(record);
            }

            record.Messages.Add(formattedMessage);

            // Keep only the last 5 messages to save memory
            if (record.Messages.Count > 5)
            {
                record.Messages.RemoveAt(0);
            }
        }

        #endregion
    }
}