using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

[Info("Admin plugin", "jEMIXS", "1.0.0")]
 public class AdminManager : OxygenPlugin
    {
        // GRANT 
        // syntax: /grant <user/group> <SteamID/GroupName> <permission>
        [Command("grant")]
        [Permission("*")] // only super admin can user
        private async Task GrantCommand(PlayerBase player, string[] args)
        {
            if (args.Length < 3)
            {
                player.Reply("Usage: /grant <user|group> <target> <permission>");
                return;
            }

            string type = args[0].ToLower();
            string target = args[1]; // SteamID or name group
            string perm = args[2];

            if (type == "user")
            {
                PermissionManager.GrantUserPermission(target, perm);
                player.Reply($"Granted permission '{perm}' to User {target}");
            }
            else if (type == "group")
            {
                PermissionManager.GrantGroupPermission(target, perm);
                player.Reply($"Granted permission '{perm}' to Group {target}");
            }
            else
            {
                player.Reply("Error: Invalid type. Use 'user' or 'group'.");
            }
        }

        // REVOKE
        // syntax: /revoke <user/group> <SteamID/GroupName> <permission>
        [Command("revoke")]
        [Permission("*")]
        private async Task RevokeCommand(PlayerBase player, string[] args)
        {
            if (args.Length < 3)
            {
                player.Reply("Usage: /revoke <user|group> <target> <permission>");
                return;
            }

            string type = args[0].ToLower();
            string target = args[1];
            string perm = args[2];

            if (type == "user")
            {
                PermissionManager.RevokeUserPermission(target, perm);
                player.Reply($"Revoked permission '{perm}' from User {target}");
            }
            else if (type == "group")
            {
                PermissionManager.RevokeGroupPermission(target, perm);
                player.Reply($"Revoked permission '{perm}' from Group {target}");
            }
        }

        // GROUP manage
        // syntax: /group <add/remove> <SteamID> <GroupName>
        [Command("group")]
        [Permission("*")]
        private async Task GroupCommand(PlayerBase player, string[] args)
        {
            if (args.Length < 3)
            {
                player.Reply("Usage: /group <add|remove> <SteamID> <GroupName>");
                return;
            }

            string action = args[0].ToLower();
            string steamId = args[1];
            string groupName = args[2];

            if (action == "add")
            {
                PermissionManager.AddUserToGroup(steamId, groupName);
                player.Reply($"User {steamId} ADDED to group '{groupName}'");
            }
            else if (action == "remove")
            {
                PermissionManager.RemoveUserFromGroup(steamId, groupName);
                player.Reply($"User {steamId} REMOVED from group '{groupName}'");
            }
        }
    }