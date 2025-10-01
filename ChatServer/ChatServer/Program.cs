using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;


var listener = new TcpListener(IPAddress.Any, 5000);
var clients = new Dictionary<TcpClient, string>();
var semaphore = new SemaphoreSlim(1, 1);

try
{
    listener.Start();
    Console.WriteLine($"[LOG] Server started at {listener.LocalEndpoint}");

    while (true)
    {
        var client = await listener.AcceptTcpClientAsync();
        Console.WriteLine($"[LOG] Client from {client.Client.RemoteEndPoint} attempting to connect.");

        _ = HandleClientAsync(client);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[ERROR] Server error: {ex.Message}");
    File.AppendAllText("server_log.txt", $"[ERROR] {DateTime.Now}: Server error: {ex.Message}\n");
}
finally
{
    listener.Stop();
}


async Task HandleClientAsync(TcpClient client)
{
    var stream = client.GetStream();
    string username = "Anonim";
    byte[] buffer = new byte[1024];

    try
    {
        int bytesRead = await stream.ReadAsync(buffer);
        string initialMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

        if (initialMessage.StartsWith("/user "))
        {
            username = initialMessage[6..];

            await semaphore.WaitAsync();
            try
            {
                if (clients.ContainsValue(username))
                {
                    byte[] reject = Encoding.UTF8.GetBytes("[SYSTEM] Username already taken. Connection rejected.");
                    await stream.WriteAsync(reject);
                    client.Close();
                    Console.WriteLine($"[LOG] Connection from {client.Client.RemoteEndPoint} rejected (duplicate username).");
                    File.AppendAllText("server_log.txt", $"[LOG] {DateTime.Now}: Connection from {client.Client.RemoteEndPoint} rejected (duplicate username).\n");
                    return;
                }
                clients.Add(client, username);
                Console.WriteLine($"[LOG] Client '{username}' connected.");
                File.AppendAllText("server_log.txt", $"[LOG] {DateTime.Now}: Client '{username}' connected.\n");
            }
            finally
            {
                semaphore.Release();
            }

            await BroadcastMessage($"[SYSTEM] {username} has joined.", null);
            await SendUserListToAllClients();
        }

        while (client.Connected)
        {
            bytesRead = await stream.ReadAsync(buffer);
            if (bytesRead == 0) break;

            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            if (message.StartsWith("/w "))
            {
                var parts = message[3..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    string targetUsername = parts[0];
                    string privateMessage = parts[1];
                    await SendMessage(client, $"[PM to {targetUsername}] {username}: {privateMessage}", targetUsername);

                    Console.WriteLine($"[CHAT] PM from {username} to {targetUsername}: {privateMessage}");
                    File.AppendAllText("server_log.txt", $"[CHAT] {DateTime.Now}: PM from {username} to {targetUsername}: {privateMessage}\n");
                }
            }
            else if (message.StartsWith("/"))
            {
                if (message == "/typing-start")
                {
                    await BroadcastMessageWithPrefix("/typing-start", username, client);
                }
                else if (message == "/typing-end")
                {
                    await BroadcastMessageWithPrefix("/typing-end", username, client);
                }
            }
            else
            {
                Console.WriteLine($"[CHAT] {username}: {message}");
                File.AppendAllText("server_log.txt", $"[CHAT] {DateTime.Now}: {username}: {message}\n");
                await BroadcastMessage($"{username}: {message}", client);
            }
        }
    }
    catch (IOException)
    {
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Error handling client '{username}': {ex.Message}");
        File.AppendAllText("server_log.txt", $"[ERROR] {DateTime.Now}: Error handling client '{username}': {ex.Message}\n");
    }
    finally
    {
        Console.WriteLine($"[LOG] Client '{username}' disconnected.");
        File.AppendAllText("server_log.txt", $"[LOG] {DateTime.Now}: Client '{username}' disconnected.\n");

        await semaphore.WaitAsync();
        try
        {
            clients.Remove(client);
        }
        finally
        {
            semaphore.Release();
        }

        await BroadcastMessage($"[SYSTEM] {username} has left.", null);
        await SendUserListToAllClients();

        stream?.Dispose();
        client?.Close();
    }
}

async Task BroadcastMessage(string message, TcpClient? excludeClient)
{
    byte[] buffer = Encoding.UTF8.GetBytes(message);
    await semaphore.WaitAsync();
    try
    {
        foreach (var kvp in clients)
        {
            if (kvp.Key != excludeClient)
            {
                try
                {
                    await kvp.Key.GetStream().WriteAsync(buffer);
                }
                catch (Exception) { }
            }
        }
    }
    finally
    {
        semaphore.Release();
    }
}

async Task BroadcastMessageWithPrefix(string prefix, string username, TcpClient? excludeClient)
{
    string message = $"{prefix} {username}";
    byte[] buffer = Encoding.UTF8.GetBytes(message);
    await semaphore.WaitAsync();
    try
    {
        foreach (var kvp in clients)
        {
            if (kvp.Key != excludeClient)
            {
                try
                {
                    await kvp.Key.GetStream().WriteAsync(buffer);
                }
                catch (Exception) { }
            }
        }
    }
    finally
    {
        semaphore.Release();
    }
}

async Task SendMessage(TcpClient senderClient, string message, string targetUsername)
{
    byte[] buffer = Encoding.UTF8.GetBytes(message);
    byte[] notFoundBuffer = Encoding.UTF8.GetBytes($"[SYSTEM] User '{targetUsername}' not found.");

    await semaphore.WaitAsync();
    try
    {
        var targetClient = clients.FirstOrDefault(x => x.Value.Equals(targetUsername, StringComparison.OrdinalIgnoreCase)).Key;

        if (targetClient != null)
        {
            try
            {
                await targetClient.GetStream().WriteAsync(buffer);
                await senderClient.GetStream().WriteAsync(buffer);
            }
            catch (Exception) { }
        }
        else
        {
            try
            {
                await senderClient.GetStream().WriteAsync(notFoundBuffer);
            }
            catch (Exception) { }
        }
    }
    finally
    {
        semaphore.Release();
    }
}

async Task SendUserListToAllClients()
{
    await semaphore.WaitAsync();
    try
    {
        string userList = "/users " + string.Join(",", clients.Values);
        byte[] buffer = Encoding.UTF8.GetBytes(userList);

        foreach (var kvp in clients)
        {
            try
            {
                await kvp.Key.GetStream().WriteAsync(buffer);
            }
            catch (Exception) { }
        }
    }
    finally
    {
        semaphore.Release();
    }
}