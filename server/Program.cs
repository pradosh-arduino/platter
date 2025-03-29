using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using System.Security.Cryptography.X509Certificates;
using Spectre.Console;

namespace ChatServer
{
    class Server
    {
        private static TcpListener? listener;
        private static List<TcpClient> clients = new List<TcpClient>();
        private static object clientsLock = new object();

        static string json = "";
        static dynamic? settings = null;

        static void Main(string[] args)
        {
            Console.Clear();

            var intro_table = new Table();

            intro_table.AddColumn("[blue bold]Introduction[/]");

            intro_table.AddRow("[green]Welcome[/] to [red]server-side[/] application of platter!");
            intro_table.AddRow("This is a [red]simple chat server[/] that allows multiple clients to connect and communicate with each other.\n");
            intro_table.AddRow("Check out GitHub repository → [blue]https://github.com/pradosh-arduino/platter[/]");

            AnsiConsole.Write(intro_table);

            try
            {
                string ip_address = "";
                int port = 0;

                if (!File.Exists("settings.json"))
                {
                    AnsiConsole.Write(new Rule("[bold blue]Server information.[/]").LeftJustified());

                    ip_address = AnsiConsole.Prompt(new TextPrompt<string>("Enter [red]server[/] IP Address →"));
                    port = AnsiConsole.Prompt(new TextPrompt<int>("Enter [red]server[/] port →"));

                    listener = new TcpListener(IPAddress.Parse(ip_address), port);
                    listener.Start();
                } else {
                    json = File.ReadAllText("settings.json");
                    settings = JsonConvert.DeserializeObject(json);

                    ip_address = settings["ip_address"];
                    port = settings["port"];

                    listener = new TcpListener(IPAddress.Parse(ip_address), port);
                    listener.Start();
                }

                listener.Server.SendTimeout = 5000; // 5 seconds


                AnsiConsole.MarkupLine($"[red]Server[/] [green]started[/] [grey]{ip_address.ToString().Replace("]", "").Replace("[", "")}:{port.ToString().Replace("]", "").Replace("[", "")}[/]. Waiting for connections...");
                
                AnsiConsole.MarkupLine("[grey italic]You can type message to broadcast to all members. Type something and press[/] [green italic]Enter[/]");

                Thread operator_thread = new Thread(handle_operator_thread);
                operator_thread.Start();

                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();

                    client.Client.ReceiveBufferSize = 1024;
                    client.Client.SendBufferSize = 1024;

                    lock (clientsLock)
                    {
                        if(json.ToString().Length > 0){
                            if(int.Parse(settings["max_connections"].ToString()) == 0){
                                // print_info("Max clients not set.");
                            } else if(clients.Count >= int.Parse(settings["max_connections"].ToString())){
                                print_info("Max clients reached.");
                                send_message("[red]Server[/] is full!", client);
                                client.Close();
                                continue;
                            }
                        }
                        clients.Add(client);
                    }
                    AnsiConsole.MarkupLine($"[grey]Client connected: {client.Client.RemoteEndPoint}[/]");


                    Thread clientThread = new Thread(handle_client);
                    clientThread.Start(client);
                }
            }
            catch (Exception ex)
            {
                print_error(ex.Message);
            }
            finally
            {
                listener?.Stop();
            }
        }

        static void print_error(string message){
            AnsiConsole.MarkupLine($"[bold red]Error :[/] [grey]{message.Replace("[", "[[").Replace("]", "]]")}[/]");
        }

        static void print_info(string message){
            AnsiConsole.MarkupLine($"[bold blue]Info :[/] [grey]{message.Replace("[", "[[").Replace("]", "]]")}[/]");
        }

        static void handle_operator_thread(){

            while(true){
                string? command = Console.ReadLine();
                if(command == "/exit" || command == "/quit" || command == "/stop"){
                    AnsiConsole.MarkupLine($"[red]Server[/] [red]stopped[/] [grey]Closed existing connections.[/]");
                    Environment.Exit(0);
                } else if(command.Trim().StartsWith("/")){
                    AnsiConsole.MarkupLine($"[red]Unknown command.[/] [grey italic](Only you can see this message, members cannot.)[/]");
                    var commands_table = new Table();

                    commands_table.AddColumn("[blue bold]Command name[/]");
                    commands_table.AddColumn(new TableColumn("[blue bold]Explanation[/]"));

                    commands_table.AddRow("[green]/exit[/]", "[grey]Closes all the connections, cleanly stops the server and exits.[/]");
                    commands_table.AddRow("[green]/quit[/]", "[grey]Same as[/] [green italic]/exit[/]");
                    commands_table.AddRow("[green]/stop[/]", "[grey]Same as[/] [green italic]/exit[/]");

                    AnsiConsole.Write(commands_table);
                } else if(string.IsNullOrEmpty(command)){
                    continue;
                } else {
                    broadcast_message($"[red bold]SERVER =>[/] {command}", null);
                }
            }
        }

        static void handle_client(object clientObj)
        {
            TcpClient client = (TcpClient)clientObj;
            NetworkStream? stream = null;
            string clientName = "<unknown>";

            try
            {
                stream = client.GetStream();
                byte[] buffer = new byte[1024];
                int bytesRead;

                // Get the client's name
                bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    clientName = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                    broadcast_message($"[yellow bold]{clientName} has joined the chat.[/]", client);
                    AnsiConsole.MarkupLine($"[grey]{client.Client.RemoteEndPoint} => {clientName}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"Client [grey]{client.Client.RemoteEndPoint}[/] [red]disconnected[/] before sending name.");
                    return;
                }

                while (true)
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string message = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();
                        AnsiConsole.MarkupLine($"[bold grey]LOGS:[/] {clientName} => {message}");

                        if(message.Trim() == "/ping"){
                            send_message("[green]Pong![/]", client);
                        } else if(message.Trim() == "/members"){
                            int k = 0;

                            lock (clientsLock)
                            {
                                for (k = 0; k < clients.Count; k++);
                            }

                            send_message($"There are [blue]{k}[/] members [green]online[/].", client);

                        } else {
                            broadcast_message($"[grey]{clientName} =>[/] {message}", client);
                        }
                    }
                    else
                    {
                        break; // Client disconnected
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client {clientName}: {ex.Message}");
            }
            finally
            {
                lock (clientsLock)
                {
                    clients.Remove(client);
                }
                broadcast_message($"[yellow bold]{clientName} has left the chat.[/]", null);
                AnsiConsole.MarkupLine($"[gray]Client disconnected: {clientName} ({client.Client.RemoteEndPoint})[/]");
                stream?.Close();
                client?.Close();
            }
        }

        static void send_message(string message, TcpClient target_client){
            byte[] data = Encoding.ASCII.GetBytes(message);
            
            try
            {
                NetworkStream stream = target_client.GetStream();
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                print_error($"Error broadcasting to client {target_client.Client.RemoteEndPoint}: {ex.Message}");
                // Remove the client if there's an error
                clients.Remove(target_client);
            }
        }

        static void broadcast_message(string message, TcpClient sender_client)
        {
            byte[] data = Encoding.ASCII.GetBytes(message);
            lock (clientsLock)
            {
                foreach (TcpClient client in clients)
                {
                    if (client != sender_client) // Don't send the message back to the sender
                    {
                        try
                        {
                            NetworkStream stream = client.GetStream();
                            stream.Write(data, 0, data.Length);
                        }
                        catch (Exception ex)
                        {
                            print_error($"Error broadcasting to client {client.Client.RemoteEndPoint}: {ex.Message}");
                            // Remove the client if there's an error
                            clients.Remove(client);
                        }
                    }
                }
            }
        }
    }
}