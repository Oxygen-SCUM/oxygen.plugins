using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

[Info("Web RCON 2.0", "jEMIXS", "1.7.0")]
public class WebRconPlugin : OxygenPlugin
{
    string rconPassword = "SuperAdmin123";
    int rconPort = 28015;

    private HttpListener _listener;
    private bool _isRunning;
    
    private readonly List<WebSocket> _activeClients = new List<WebSocket>();
    private readonly object _clientsLock = new object();

    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public override void OnLoad()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{rconPort}/"); 
            _listener.Start();
            
            _isRunning = true;
            Task.Run(ListenForClientsAsync);
            
            Console.WriteLine($"[WebRcon] RCON 2.0 started on port {rconPort}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebRcon] Failed to start: {ex.Message}");
        }
    }

    public override void OnUnload()
    {
        _isRunning = false;
        _listener?.Stop();
        _listener?.Close();
        
        lock (_clientsLock)
        {
            _activeClients.Clear();
        }
    }

    private async Task ListenForClientsAsync()
    {
        while (_isRunning)
        {
            try
            {
                var context = await _listener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    string pathPassword = context.Request.RawUrl.Trim('/');

                    if (pathPassword == rconPassword)
                    {
                        var wsContext = await context.AcceptWebSocketAsync(null);
                        
                        lock (_clientsLock) { _activeClients.Add(wsContext.WebSocket); }
                        
                        _ = Task.Run(() => HandleClientAsync(wsContext.WebSocket));
                    }
                    else
                    {
                        context.Response.StatusCode = 401;
                        context.Response.Close();
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (HttpListenerException) { break; }
            catch { }
        }
    }

    private async Task HandleClientAsync(WebSocket webSocket)
    {
        var buffer = new byte[4096];

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                    break;
                }

                string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                
                try 
                {
                    var requestData = JsonSerializer.Deserialize<WebRconRequest>(message);

                    if (requestData != null && !string.IsNullOrEmpty(requestData.Message))
                    {
                        string command = requestData.Message.Trim();

                        if (command.Equals("playerlist", StringComparison.OrdinalIgnoreCase))
                        {
                            string jsonResponse = GetJsonPlayersList();
                            await SendToClientAsync(webSocket, jsonResponse, requestData.Identifier, "Generic");
                        }
                        else if (command.Equals("status", StringComparison.OrdinalIgnoreCase) || command.Equals("serverinfo", StringComparison.OrdinalIgnoreCase))
                        {
                            string jsonResponse = GetJsonServerStatus();
                            await SendToClientAsync(webSocket, jsonResponse, requestData.Identifier, "Generic");
                        }
                        else if (command.StartsWith("clearinv ", StringComparison.OrdinalIgnoreCase))
                        {
                            string response = HandleClearInvCommand(command);
                            await SendToClientAsync(webSocket, response, requestData.Identifier, "Generic");
                        }
                        else
                        {
                            var cmdResult = await Server.ProcessCommandAsync(command);
                            await SendToClientAsync(webSocket, cmdResult.Message, requestData.Identifier, "Generic");
                        }
                    }
                }
                catch (JsonException) { }
            }
        }
        catch { }
        finally
        {
            lock (_clientsLock) { _activeClients.Remove(webSocket); }
            
            if (webSocket.State != WebSocketState.Closed)
                webSocket.Abort();
        }
    }

    private string GetJsonPlayersList()
    {
        if (Server.AllPlayers == null)
            return "[]";

        var playerList = new List<JsonPlayerInfo>();

        foreach (var p in Server.AllPlayers)
        {
            var pInfo = new JsonPlayerInfo
            {
                Name = p.Name,
                FakeName = p.FakeName,
                SteamId = p.SteamId,
                DatabaseId = p.DatabaseId,
                IpAddress = p.IpAddress,
                Ping = p.Ping,
                LocationX = p.Location.X,
                LocationY = p.Location.Y,
                LocationZ = p.Location.Z,
                FamePoints = p.FamePoints,
                Money = p.Money,
                Gold = p.Gold,
                InGameEvent = p.InGameEvent,
                Inventory = new List<JsonItemInfo>()
            };

            if (p.Inventory != null && p.Inventory.All != null)
            {
                var allChildren = new HashSet<Item>();
                foreach (var item in p.Inventory.All)
                {
                    if (item.Contents != null)
                    {
                        foreach (var child in item.Contents)
                        {
                            allChildren.Add(child);
                        }
                    }
                }

                foreach (var item in p.Inventory.All)
                {
                    if (!allChildren.Contains(item))
                    {
                        pInfo.Inventory.Add(new JsonItemInfo
                        {
                            ItemName = item.Name,
                            ContainsCount = item.Contents != null ? item.Contents.Count : 0
                        });
                    }
                }
            }

            playerList.Add(pInfo);
        }

        return JsonSerializer.Serialize(playerList, _jsonOptions);
    }

    private string GetJsonServerStatus()
    {
        var info = new JsonServerInfo
        {
            PlayersOnline = Server.AllPlayers != null ? Server.AllPlayers.Count : 0,
            UptimeSeconds = (int)Server.OutServerTime,
            TimeOfDay = Server.TimeOfDay,
            TimeMultiplier = Server.TimeMultiplier,
            SunriseTime = Server.SunriseTime,
            SunsetTime = Server.SunsetTime,
            RainCloudsCoverage = Server.RainCloudsCoverage,
            WindSpeed = Server.WindSpeed
        };

        return JsonSerializer.Serialize(info, _jsonOptions);
    }

    private string HandleClearInvCommand(string command)
    {
        string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length < 2)
        {
            return "Usage: clearinv <SteamID>";
        }

        string targetId = parts[1];
        var targetPlayer = Server.AllPlayers?.Find(p => p.SteamId == targetId);

        if (targetPlayer == null)
        {
            return $"Error: Player with SteamID {targetId} is not online.";
        }

        if (targetPlayer.Inventory != null)
        {
            int deletedCount = targetPlayer.Inventory.Clear();
            return $"Successfully deleted {deletedCount} items from {targetPlayer.Name}'s inventory.";
        }

        return $"Error: Could not access inventory for {targetPlayer.Name}.";
    }

    private async Task SendToClientAsync(WebSocket client, string message, int identifier, string type)
    {
        if (client.State != WebSocketState.Open) return;

        var responseData = new WebRconResponse
        {
            Identifier = identifier,
            Message = message,
            Type = type
        };

        string jsonResponse = JsonSerializer.Serialize(responseData);
        byte[] responseBytes = Encoding.UTF8.GetBytes(jsonResponse);

        try
        {
            await client.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
    }

    public async Task BroadcastToRconAsync(string message, string type = "Generic")
    {
        List<WebSocket> clientsSnapshot;
        lock (_clientsLock)
        {
            clientsSnapshot = new List<WebSocket>(_activeClients);
        }

        foreach (var client in clientsSnapshot)
        {
            await SendToClientAsync(client, message, 0, type); 
        }
    }

    public override void OnPlayerConnected(PlayerBase player)
    {
        string logMsg = $"[LOGIN] {player.Name} ({player.SteamId}) connected from {player.IpAddress}.";
        _ = BroadcastToRconAsync(logMsg, "Generic");
    }

    public override void OnPlayerDisconnected(PlayerBase player)
    {
        string logMsg = $"[LOGOUT] {player.Name} ({player.SteamId}) disconnected.";
        _ = BroadcastToRconAsync(logMsg, "Generic");
    }

    public override bool OnPlayerChat(PlayerBase player, string message, int chatType)
    {
        string chatTypeName = chatType switch
        {
            1 => "Local",
            2 => "Global",
            3 => "Squad",
            4 => "Admin",
            _ => "Default"
        };

        string formatMsg = $"[{chatTypeName}] {player.Name}: {message}";
        _ = BroadcastToRconAsync(formatMsg, "Chat");

        return true; 
    }

    public override void OnPlayerRespawn(PlayerBase player, PlayerRespawnData data)
    {
        string sectorName = $"{(char)('A' + data.SectorX)}{data.SectorY}";
        
        string spawnMethod = data.SpawnLocationType switch
        {
            1 => "Random",
            2 => "Sector",
            3 => "Home",
            4 => "Squad",
            5 => "Event",
            _ => "Unknown"
        };

        string logMsg = $"[RESPAWN] {player.Name} spawned in {sectorName} (Method: {spawnMethod})";
        _ = BroadcastToRconAsync(logMsg, "Generic");
    }

    // --- RCON PROTOCOL CLASSES ---

    public class WebRconRequest
    {
        public int Identifier { get; set; }
        public string Message { get; set; }
        public string Name { get; set; }
    }

    public class WebRconResponse
    {
        public string Message { get; set; }
        public int Identifier { get; set; }
        public string Type { get; set; }
    }

    // --- JSON DATA CLASSES ---

    public class JsonPlayerInfo
    {
        public string Name { get; set; }
        public string FakeName { get; set; }
        public string SteamId { get; set; }
        public int DatabaseId { get; set; }
        public string IpAddress { get; set; }
        public int Ping { get; set; }
        public float LocationX { get; set; }
        public float LocationY { get; set; }
        public float LocationZ { get; set; }
        public int FamePoints { get; set; }
        public int Money { get; set; }
        public int Gold { get; set; }
        public int InGameEvent { get; set; }
        public List<JsonItemInfo> Inventory { get; set; }
    }

    public class JsonItemInfo
    {
        public string ItemName { get; set; }
        public int ContainsCount { get; set; }
    }

    public class JsonServerInfo
    {
        public int PlayersOnline { get; set; }
        public int UptimeSeconds { get; set; }
        public float TimeOfDay { get; set; }
        public float TimeMultiplier { get; set; }
        public float SunriseTime { get; set; }
        public float SunsetTime { get; set; }
        public float RainCloudsCoverage { get; set; }
        public float WindSpeed { get; set; }
    }
}