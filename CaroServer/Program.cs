using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

class CaroServer
{
    private static TcpListener server = null;
    private static Dictionary<int, List<ClientHandler>> waitingQueues = new Dictionary<int, List<ClientHandler>>();
    private static object lockObj = new object();

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        int port = 8888;
        server = new TcpListener(IPAddress.Any, port);
        server.Start();
        Console.WriteLine($"[SERVER] Listening on port {port}...");

        try
        {
            while (true)
            {
                TcpClient tcpClient = server.AcceptTcpClient();
                ClientHandler clientHandler = new ClientHandler(tcpClient);
                Thread clientThread = new Thread(clientHandler.ProcessClient);
                clientThread.Start();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
        }
        finally
        {
            server.Stop();
        }
    }

    public static void MatchClients(ClientHandler client, int boardSize)
    {
        lock (lockObj)
        {
            if (!waitingQueues.ContainsKey(boardSize))
            {
                waitingQueues[boardSize] = new List<ClientHandler>();
            }

            var queue = waitingQueues[boardSize];
            queue.Add(client);

            if (queue.Count >= 2)
            {
                ClientHandler playerX = queue[0];
                ClientHandler playerO = queue[1];
                queue.RemoveRange(0, 2);

                Room room = new Room(playerX, playerO);
                playerX.CurrentRoom = room;
                playerO.CurrentRoom = room;

                playerX.SendMessage("LOGIN_SUCCESS");
                playerX.SendMessage("START|X");

                playerO.SendMessage("LOGIN_SUCCESS");
                playerO.SendMessage("START|O");

                Console.WriteLine($"[INFO] Successful board game pairing: {boardSize}x{boardSize}: {playerX.Username} (X) vs {playerO.Username} (O)");
            }
            else
            {
                client.SendMessage("LOGIN_SUCCESS");
                client.SendMessage($"WAIT|Waiting for a board game player: {boardSize}x{boardSize}...");
                Console.WriteLine($"[INFO] {client.Username} waiting to be seated at a shared table: {boardSize}x{boardSize}...");
            }
        }
    }

    public static void RemoveClient(ClientHandler client)
    {
        lock (lockObj)
        {
            foreach (var queue in waitingQueues.Values)
            {
                if (queue.Contains(client))
                {
                    queue.Remove(client);
                    break;
                }
            }
        }
    }
}

class Room
{
    public ClientHandler PlayerX { get; set; }
    public ClientHandler PlayerO { get; set; }

    public Room(ClientHandler x, ClientHandler o)
    {
        PlayerX = x;
        PlayerO = o;
    }
}

class ClientHandler
{
    private TcpClient tcpClient;
    private StreamReader reader;
    private StreamWriter writer;
    public string Username { get; private set; } = "Player";
    public Room? CurrentRoom { get; set; }

    public ClientHandler(TcpClient client)
    {
        tcpClient = client;
        NetworkStream stream = client.GetStream();
        reader = new StreamReader(stream);
        writer = new StreamWriter(stream) { AutoFlush = true };
    }

    public void SendMessage(string message)
    {
        try { writer.WriteLine(message); } catch { }
    }

    public void ProcessClient()
    {
        try
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] parts = line.Split('|');
                string command = parts[0];

                switch (command)
                {
                    case "LOGIN":
                        Username = parts[1];
                        int boardSize = int.Parse(parts[2]);
                        CaroServer.MatchClients(this, boardSize);
                        break;

                    case "MOVE":
                        int row = int.Parse(parts[1]);
                        int col = int.Parse(parts[2]);
                        if (CurrentRoom != null)
                        {
                            ClientHandler? opponent = (CurrentRoom.PlayerX == this) ? CurrentRoom.PlayerO : CurrentRoom.PlayerX;
                            opponent?.SendMessage($"MOVE|{row}|{col}");
                        }
                        break;

                    case "WIN":
                        if (CurrentRoom != null)
                        {
                            ClientHandler? opponent = (CurrentRoom.PlayerX == this) ? CurrentRoom.PlayerO : CurrentRoom.PlayerX;
                            opponent?.SendMessage(line);
                        }
                        break;

                    // --- XỬ LÝ TÍNH NĂNG CHƠI LẠI (REMATCH) ---
                    case "REMATCH":
                        if (CurrentRoom != null)
                        {
                            ClientHandler? opponent = (CurrentRoom.PlayerX == this) ? CurrentRoom.PlayerO : CurrentRoom.PlayerX;
                            opponent?.SendMessage("REMATCH_REQUEST");
                        }
                        break;

                    case "REMATCH_ACCEPT":
                        if (CurrentRoom != null)
                        {
                            // Khi cả 2 đồng ý chơi lại, đổi vai trò X và O cho công bằng ở ván mới
                            ClientHandler playerX = CurrentRoom.PlayerX;
                            ClientHandler playerO = CurrentRoom.PlayerO;

                            // Đổi chỗ PlayerX và PlayerO trong phòng cho ván mới
                            CurrentRoom.PlayerX = playerO;
                            CurrentRoom.PlayerO = playerX;

                            // Gửi lệnh reset và cập nhật lại ký tự mới cho từng người
                            CurrentRoom.PlayerX.SendMessage("RESET_BOARD|X");
                            CurrentRoom.PlayerO.SendMessage("RESET_BOARD|O");
                        }
                        break;

                    case "REMATCH_DECLINE":
                        if (CurrentRoom != null)
                        {
                            ClientHandler? opponent = (CurrentRoom.PlayerX == this) ? CurrentRoom.PlayerO : CurrentRoom.PlayerX;
                            opponent?.SendMessage("REMATCH_DECLINE");
                        }
                        break;
                }
            }
        }
        catch { }
        finally
        {
            CaroServer.RemoveClient(this);
        }
    }
}