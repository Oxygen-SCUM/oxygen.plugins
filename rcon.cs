using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

[Info("Custom RCON", "jEMIXS", "1.0.0")]
public class CustomRconPlugin : OxygenPlugin
{
    private RconServer _rcon;

    public override void OnLoad()
    {
        Console.WriteLine("[RCON Plugin] init...");

        try
        {
            _rcon = new RconServer(28015, "SuperAdmin123"); // rcon port and password

            _rcon.OnCommandReceived += HandleRconCommand;

            _rcon.Start();
            
            Console.WriteLine("[RCON Plugin] started 28015!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RCON Plugin] fail: {ex.Message}");
        }
    }

    public override void OnUnload()
    {
        Console.WriteLine("[RCON Plugin] stopped...");
        
        if (_rcon != null)
        {
            _rcon.OnCommandReceived -= HandleRconCommand;
            _rcon.Stop();
            _rcon = null;
        }
    }

    private async Task<string> HandleRconCommand(string command)
    {
        Console.WriteLine($"[RCON] received command: {command}");
        var result = await Server.ProcessCommandAsync(command);

        return result.Message;
    }
}

public class RconServer
{
    private const int SERVERDATA_AUTH = 3;
    private const int SERVERDATA_EXECCOMMAND = 2;
    private const int SERVERDATA_AUTH_RESPONSE = 2;
    private const int SERVERDATA_RESPONSE_VALUE = 0;

    private TcpListener _listener;
    private bool _isRunning;
    private readonly int _port;
    private readonly string _password;

    public event Func<string, Task<string>> OnCommandReceived;

    public RconServer(int port, string password)
    {
        _port = port;
        _password = password;
    }

    public void Start()
    {
        if (_isRunning) return;

        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _isRunning = true;

        Task.Run(AcceptClientsAsync);
    }

    public void Stop()
    {
        _isRunning = false;
        _listener?.Stop();
    }

    private async Task AcceptClientsAsync()
    {
        while (_isRunning)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Console.WriteLine($"[RCON] FAIL: {ex.Message}"); }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
{
    using NetworkStream stream = client.GetStream();
    bool isAuthenticated = false;

    try
    {
        while (client.Connected && _isRunning)
        {
            byte[] sizeBuffer = new byte[4];
            int bytesRead = await stream.ReadAsync(sizeBuffer, 0, 4);
            if (bytesRead < 4) break;

            int packetSize = BitConverter.ToInt32(sizeBuffer, 0);
            if (packetSize < 10 || packetSize > 4096) break;

            byte[] packetBuffer = new byte[packetSize];
            int read = 0;
            
            while (read < packetSize)
            {
                int currentRead = await stream.ReadAsync(packetBuffer, read, packetSize - read);
                if (currentRead == 0) break;
                read += currentRead;
            }
            if (read < packetSize) break; 

            using MemoryStream ms = new MemoryStream(packetBuffer);
            using BinaryReader reader = new BinaryReader(ms, Encoding.UTF8);

            int requestId = reader.ReadInt32();
            int requestType = reader.ReadInt32();
            string body = ReadNullTerminatedString(reader);

            if (requestType == SERVERDATA_AUTH)
            {
                if (body == _password)
                {
                    isAuthenticated = true;
                    await SendPacketAsync(stream, requestId, SERVERDATA_AUTH_RESPONSE, "");
                }
                else
                {
                    await SendPacketAsync(stream, -1, SERVERDATA_AUTH_RESPONSE, "");
                    break;
                }
            }
            else if (requestType == SERVERDATA_EXECCOMMAND)
            {
                if (!isAuthenticated) break;

                string responseMessage = "Receive command but not exec.\n";
                if (OnCommandReceived != null)
                {
                    try { responseMessage = await OnCommandReceived.Invoke(body); }
                    catch (Exception ex) { responseMessage = $"Fail: {ex.Message}\n"; }
                }

                await SendPacketAsync(stream, requestId, SERVERDATA_RESPONSE_VALUE, responseMessage);
            }
            else if (requestType == SERVERDATA_RESPONSE_VALUE)
            {
                await SendPacketAsync(stream, requestId, SERVERDATA_RESPONSE_VALUE, body);
            }
        }
    }
    catch { }
    finally { client.Close(); }
}

    private async Task SendPacketAsync(NetworkStream stream, int id, int type, string body)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        int packetSize = 4 + 4 + bodyBytes.Length + 1 + 1;

        using MemoryStream ms = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(ms);

        writer.Write(packetSize);
        writer.Write(id);
        writer.Write(type);
        writer.Write(bodyBytes);
        writer.Write((byte)0);
        writer.Write((byte)0);

        byte[] finalPacket = ms.ToArray();
        await stream.WriteAsync(finalPacket, 0, finalPacket.Length);
    }

    private string ReadNullTerminatedString(BinaryReader reader)
    {
        List<byte> bytes = new List<byte>();
        byte b;
        while (reader.BaseStream.Position < reader.BaseStream.Length && (b = reader.ReadByte()) != 0)
        {
            bytes.Add(b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}