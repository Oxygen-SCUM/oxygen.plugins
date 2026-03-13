using System;
using System.Text.Json;
// Core namespaces required for Oxygen API development
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;
// Required to use the native non-blocking HTTP client
using Oxygen.Csharp.Web; 

namespace AntiVpnSystem
{
    // The configuration class for the plugin.
    public class AntiVpnConfig
    {
        public bool Enabled { get; set; } = true;
        public string KickMessage { get; set; } = "VPN or Proxy connections are not allowed on this server.";
        public string BypassPermission { get; set; } = "antivpn.bypass"; // Permission to ignore this check
    }

    // Data Transfer Object (DTO) used to deserialize the JSON response
    public class IpApiResponse
    {
        public bool proxy { get; set; }   // True if the IP is a known VPN, proxy, or Tor node
        public bool hosting { get; set; } // True if the IP belongs to a server hosting provider
    }

    [Info("Anti-VPN System", "Standalone", "1.2.0")]
    [Description("Example plugin that detects if a connecting player uses a VPN/Proxy using Oxygen's native HTTP client.")]
    public class AntiVpnPlugin : OxygenPlugin
    {
        private AntiVpnConfig _cfg;

        // OnLoad is triggered when the Oxygen framework initializes the plugin
        public override void OnLoad()
        {
            _cfg = LoadConfig<AntiVpnConfig>() ?? new AntiVpnConfig();
            SaveConfig(_cfg);
        }

        // Triggered automatically by Oxygen when a player joins the server.
        // We no longer need 'async' here because the native client uses callbacks.
        public override void OnPlayerConnected(PlayerBase player)
        {
            // Check if the plugin is globally enabled
            if (!_cfg.Enabled) return;
            
            // Check if the player has the bypass permission
            if (player.HasPermission(_cfg.BypassPermission)) return;

            // Prepare the URL for the free IP check API
            string url = $"http://ip-api.com/json/{player.IpAddress}?fields=proxy,hosting";

            // Use Oxygen's native, non-blocking HTTP client
            Http.Request(url)
                .Timeout(10) // Set a 10-second timeout to prevent hanging
                .Get((code, response) => 
                {
                    // Check if the HTTP request was successful (Status code 200 OK)
                    if (code == 200)
                    {
                        try
                        {
                            // Parse the JSON string response into our C# object
                            var data = JsonSerializer.Deserialize<IpApiResponse>(response);

                            // If the API flagged the IP as a proxy or hosting provider
                            if (data != null && (data.proxy || data.hosting))
                            {
                                // Send a message explaining the kick
                                player.ProcessCommand($"SendNotification 2 {player.DatabaseId} \"{_cfg.KickMessage}\"");

                                // Execute the server console command to kick the player
                                player.ProcessCommand($"kick {player.SteamId}");
                                
                                // Log the action to the server console
                                Console.WriteLine($"[Anti-VPN] Kicked {player.Name} ({player.SteamId}). Reason: VPN detected on IP {player.IpAddress}");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Catch JSON parsing errors if the API returns an unexpected format
                            Console.WriteLine($"[Anti-VPN] Failed to parse API response for {player.IpAddress}: {ex.Message}");
                        }
                    }
                    else if (code == 0)
                    {
                        // Code 0 usually means a DNS failure or the server has no internet connection
                        Console.WriteLine($"[Anti-VPN] Network error while checking IP: {player.IpAddress}");
                    }
                    else
                    {
                        // Log other HTTP errors (e.g., 404, 429 Too Many Requests, 500)
                        Console.WriteLine($"[Anti-VPN] API returned an error code ({code}) for IP {player.IpAddress}");
                    }
                });
        }
    }
}