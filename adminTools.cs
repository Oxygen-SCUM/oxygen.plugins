using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;
using Oxygen.Csharp.Web;

public class ApiResponse<T>
{
    public string status { get; set; }
    public int code { get; set; }
    public long timestamp { get; set; }
    public T data { get; set; }
}

public class CommandPayload
{
    public string command { get; set; }
}

public class PlayerCommandPayload
{
    public string steamId { get; set; }
    public string command { get; set; }
}

[Info("WebAPI", "jEMIXS", "0.0.1")]
[Description("web API plugin for SCUM server management")]
public class WebApiPlugin : OxygenPlugin
{
    private const string API_TOKEN = "SuperSecretPassword123";
    private const int API_PORT = 8448;
    private readonly JsonSerializerOptions _jsonOptions;

    public WebApiPlugin()
    {
        _jsonOptions = new JsonSerializerOptions 
        { 
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public override void OnLoad()
    {
        try 
        {
            StartWebServer(API_PORT, API_TOKEN);
            Console.WriteLine($"[WebAPI] Server started on port {API_PORT}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebAPI] Failed to start server: {ex.Message}");
        }
    }

    private string JsonResponse<T>(T payload, int statusCode = 200, string statusMsg = "success")
    {
        var response = new ApiResponse<T>
        {
            status = statusMsg,
            code = statusCode,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            data = payload
        };
        
        return JsonSerializer.Serialize(response, _jsonOptions);
    }

    [WebRoute("/heartbeat", "GET", requireAuth: false)]
    public string GetHeartbeat(string body)
    {
        var data = new { state = "alive" };
        return JsonResponse(data);
    }

    [WebRoute("/serverinfo", "GET", requireAuth: true)]
    public string GetServerInfo(string body)
    {
        var serverInfo = new 
        {
            TimeOfDay = Server.TimeOfDay,
            TimeMultiplier = Server.TimeMultiplier,
            SunriseTime = Server.SunriseTime,
            SunsetTime = Server.SunsetTime,
            RainCloudsCoverage = Server.RainCloudsCoverage,
            WindSpeed = Server.WindSpeed,
            OutServerTimeSeconds = Server.OutServerTime,
            TotalPlayersOnline = Server.AllPlayers.Count,
            TotalVehiclesSpawned = Server.AllVehicles.Count,
            TotalBasesBuilt = Server.AllFlags.Count,
            TotalSquads = Server.AllSquads.Count,
            TotalBunkers = Server.AllBunkers.Count
        };

        return JsonResponse(serverInfo);
    }

    [WebRoute("/players", "GET", requireAuth: true)]
    public string GetPlayers(string body)
    {
        return JsonResponse(Server.AllPlayers);
    }

    [WebRoute("/vehicles", "GET", requireAuth: true)]
    public string GetVehicles(string body)
    {
        return JsonResponse(Server.AllVehicles);
    }

    [WebRoute("/flags", "GET", requireAuth: true)]
    public string GetFlags(string body)
    {
        return JsonResponse(Server.AllFlags);
    }

    [WebRoute("/squads", "GET", requireAuth: true)]
    public string GetSquads(string body)
    {
        return JsonResponse(Server.AllSquads);
    }

    [WebRoute("/bunkers", "GET", requireAuth: true)]
    public string GetBunkers(string body)
    {
        return JsonResponse(Server.AllBunkers);
    }

    [WebRoute("/command", "POST", requireAuth: true)]
    public string ExecuteCommand(string body)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<CommandPayload>(body, _jsonOptions);
            
            if (payload == null || string.IsNullOrEmpty(payload.command))
            {
                return JsonResponse(new { error = "Command is required" }, 400, "error");
            }

            var cmdResult = Server.ProcessCommandAsync(payload.command).GetAwaiter().GetResult();

            var resultData = new 
            {
                success = cmdResult.Success,
                message = cmdResult.Message
            };

            return JsonResponse(resultData, cmdResult.Success ? 200 : 400, cmdResult.Success ? "success" : "failed");
        }
        catch (Exception ex)
        {
            return JsonResponse(new { error = ex.Message }, 500, "error");
        }
    }

    [WebRoute("/playercommand", "POST", requireAuth: true)]
    public string ExecutePlayerCommand(string body)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PlayerCommandPayload>(body, _jsonOptions);
            
            if (payload == null || string.IsNullOrEmpty(payload.command) || string.IsNullOrEmpty(payload.steamId))
            {
                return JsonResponse(new { error = "Both steamId and command are required" }, 400, "error");
            }

            var player = Server.AllPlayers.FirstOrDefault(p => p.SteamId == payload.steamId);

            if (player == null)
            {
                return JsonResponse(new { error = "Player not found or offline" }, 404, "error");
            }

            var cmdResult = player.ProcessCommandAsync(payload.command).GetAwaiter().GetResult();

            var resultData = new 
            {
                success = cmdResult.Success,
                message = cmdResult.Message
            };

            return JsonResponse(resultData, cmdResult.Success ? 200 : 400, cmdResult.Success ? "success" : "failed");
        }
        catch (Exception ex)
        {
            return JsonResponse(new { error = ex.Message }, 500, "error");
        }
    }
}