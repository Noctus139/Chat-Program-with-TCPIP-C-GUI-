using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static TcpListener listener;
    private static Dictionary<TcpClient, string> clients = new Dictionary<TcpClient, string>();
    private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    static async Task Main(string[] args)
    {
        listener = new TcpListener(IPAddress.Any, 5000);
        listener.Start();
        Console.WriteLine($"[LOG] Server dimulai di {listener.LocalEndpoint}");

        try
        {
            while (true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                Console.WriteLine($"[LOG] Klien dari {client.Client.RemoteEndPoint} mencoba terhubung.");

                _ = Task.Run(() => HandleClientAsync(client));
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
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        string username = "Anonim";
        byte[] buffer = new byte[1024];

        try
        {
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            string initialMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            if (initialMessage.StartsWith("/user "))
            {
                username = initialMessage.Substring("/user ".Length);

                await semaphore.WaitAsync();
                try
                {
                    if (clients.ContainsValue(username))
                    {
                        byte[] reject = Encoding.UTF8.GetBytes("[SYSTEM] Username sudah digunakan. Koneksi ditolak.");
                        await stream.WriteAsync(reject, 0, reject.Length);
                        client.Close();
                        Console.WriteLine($"[LOG] Koneksi dari {client.Client.RemoteEndPoint} ditolak (username duplikat).");
                        File.AppendAllText("server_log.txt", $"[LOG] {DateTime.Now}: Koneksi dari {client.Client.RemoteEndPoint} ditolak (username duplikat).\n");
                        return;
                    }
                    clients.Add(client, username);
                    Console.WriteLine($"[LOG] Klien '{username}' terhubung.");
                    File.AppendAllText("server_log.txt", $"[LOG] {DateTime.Now}: Klien '{username}' terhubung.\n");
                }
                finally
                {
                    semaphore.Release();
                }

                await BroadcastMessage($"[SYSTEM] {username} telah bergabung.", null);
                await SendUserListToAllClients();
            }

            while (client.Connected)
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                if (message.StartsWith("/w "))
                {
                    int spaceIndex = message.IndexOf(' ', 3);
                    if (spaceIndex > 0)
                    {
                        string targetUsername = message.Substring(3, spaceIndex - 3);
                        string privateMessage = message.Substring(spaceIndex + 1);
                        await SendMessage(client, $"[PM ke {targetUsername}] {username}: {privateMessage}", targetUsername);
                        Console.WriteLine($"[CHAT] PM dari {username} ke {targetUsername}: {privateMessage}");
                        File.AppendAllText("server_log.txt", $"[CHAT] {DateTime.Now}: PM dari {username} ke {targetUsername}: {privateMessage}\n");
                    }
                }
                else if (message == "/typing-start")
                {
                    await BroadcastMessageWithPrefix("/typing-start", username, client);
                }
                else if (message == "/typing-end")
                {
                    await BroadcastMessageWithPrefix("/typing-end", username, client);
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
            Console.WriteLine($"[ERROR] Error saat menangani klien '{username}': {ex.Message}");
            File.AppendAllText("server_log.txt", $"[ERROR] {DateTime.Now}: Error saat menangani klien '{username}': {ex.Message}\n");
        }
        finally
        {
            Console.WriteLine($"[LOG] Klien '{username}' terputus.");
            File.AppendAllText("server_log.txt", $"[LOG] {DateTime.Now}: Klien '{username}' terputus.\n");

            await semaphore.WaitAsync();
            try
            {
                clients.Remove(client);
            }
            finally
            {
                semaphore.Release();
            }

            await BroadcastMessage($"[SYSTEM] {username} telah keluar.", null);
            await SendUserListToAllClients();

            stream?.Dispose();
            client?.Close();
        }
    }

    private static async Task BroadcastMessage(string message, TcpClient excludeClient)
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
                        await kvp.Key.GetStream().WriteAsync(buffer, 0, buffer.Length);
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

    private static async Task BroadcastMessageWithPrefix(string prefix, string username, TcpClient excludeClient)
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
                        await kvp.Key.GetStream().WriteAsync(buffer, 0, buffer.Length);
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

    private static async Task SendMessage(TcpClient senderClient, string message, string targetUsername)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(message);
        byte[] notFoundBuffer = Encoding.UTF8.GetBytes($"[SYSTEM] Pengguna '{targetUsername}' tidak ditemukan.");

        await semaphore.WaitAsync();
        try
        {
            var targetClient = clients.FirstOrDefault(x => x.Value.Equals(targetUsername, StringComparison.OrdinalIgnoreCase)).Key;

            if (targetClient != null)
            {
                try
                {
                    await targetClient.GetStream().WriteAsync(buffer, 0, buffer.Length);
                    await senderClient.GetStream().WriteAsync(buffer, 0, buffer.Length);
                }
                catch (Exception) { }
            }
            else
            {
                try
                {
                    await senderClient.GetStream().WriteAsync(notFoundBuffer, 0, notFoundBuffer.Length);
                }
                catch (Exception) { }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task SendUserListToAllClients()
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
                    await kvp.Key.GetStream().WriteAsync(buffer, 0, buffer.Length);
                }
                catch (Exception) { }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }
}