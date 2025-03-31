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
        private static Dictionary<TcpClient, int> msgs_per_unit = new Dictionary<TcpClient, int>(); // client, messages sent per unit time (probably in seconds).
        private static object clientsLock = new object();
        private static HashSet<string> banned_ips = new HashSet<string>();

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

                    if(settings == null)
                        throw new Exception("Settings file is empty or invalid.");

                    if(settings["ip_address"] == null || settings["port"] == null)
                        throw new Exception("Settings file is missing `ip_address` or `port`.");

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

                    if(client == null)
                        continue;

                    client.Client.ReceiveBufferSize = 1024;
                    client.Client.SendBufferSize = 1024;

                    if(is_ip_banned(client.Client.RemoteEndPoint.ToString())){
                        send_message("[red]You have been banned from the server.[/]", client);
                        client.Close();
                        continue;
                    }

                    lock (clientsLock)
                    {
                        if(json.ToString().Length > 0){

                            if(settings == null) // Theoretically this should never happen.
                                goto no_client_limit;
                            
                            if(int.Parse(settings["max_connections"].ToString()) == 0){
                                // print_info("Max clients not set.");
                            } else if(clients.Count >= int.Parse(settings["max_connections"].ToString())){
                                send_message("[red]Server[/] is full! Please try later.", client);
                                client.Close();
                                print_info("Max clients reached. Kicked one attempted connection.");
                                continue;
                            }

                            no_client_limit:;
                        }
                        clients.Add(client);
                    }
                    AnsiConsole.MarkupLine($"[grey]Client connected: {client.Client.RemoteEndPoint}[/]");


                    Thread clientThread = new Thread(handle_client);
                    clientThread.Start(client);

                    Thread spam_protection_thread = new Thread(spam_protection);
                    spam_protection_thread.Start(client);
                }
            }
            catch (Exception ex)
            {
                print_error(ex.Message);
                print_error(ex.StackTrace);
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

        private static void handle_operator_thread(){
            string? command;

            while(true){
                command = Console.ReadLine();

                if(string.IsNullOrEmpty(command))
                    continue;

                command = command.Trim();

                if(command == "/exit" || command == "/quit" || command == "/stop"){
                    AnsiConsole.MarkupLine($"[red]Server[/] [red]stopped[/] [grey]Closed existing connections.[/]");
                    Environment.Exit(0);
                } else if(command.StartsWith("/banlist")){
                    list_banned_ips();
                }else if(command.StartsWith("/ban")){
                    string[] parts = command.Split(' ');

                    if(parts.Length < 2){
                        AnsiConsole.MarkupLine($"[red]Invalid command usage.[/] [green]/ban <ip-address>[/] [grey italic](Only you can see this message, members cannot.)[/]");
                        continue;
                    }

                    string ipAddress = parts[1];
                    if (ipAddress.Contains(":"))
                    {
                        ipAddress = ipAddress.Split(':')[0]; // Remove port if present
                    }
                    ban_ip(ipAddress);
                    print_info($"Successfully Banned IP: {ipAddress}");
                } else if(command.StartsWith("/unban")){
                    string[] parts = command.Split(' ');

                    if(parts.Length < 2){
                        AnsiConsole.MarkupLine($"[red]Invalid command usage.[/] [green]/ban <ip-address>[/] [grey italic](Only you can see this message, members cannot.)[/]");
                        continue;
                    }

                    string ipAddress = parts[1];
                    if (ipAddress.Contains(":"))
                    {
                        ipAddress = ipAddress.Split(':')[0]; // Remove port if present
                    }
                    banned_ips.Remove(ipAddress);
                    print_info($"Successfully Unbanned IP: {ipAddress}");
                } else if(command.StartsWith("/timeout")){
                    string[] parts = command.Split(' ');

                    if(parts.Length < 2){
                        AnsiConsole.MarkupLine($"[red]Invalid command usage.[/] [green]/timeout <seconds>[/] [grey italic](Only you can see this message, members cannot.)[/]");
                        continue;
                    }

                    int timeout = int.Parse(parts[1]);

                    for(int i = 0; i < timeout; i++){
                        broadcast_message($"[red]Server will shut down in {timeout - i} seconds.[/]", null);
                        Thread.Sleep(1000);
                    }

                    Environment.Exit(0);
                }
                else if(command.StartsWith("/")){
                    AnsiConsole.MarkupLine($"[red]Unknown command.[/] [grey italic](Only you can see this message, members cannot.)[/]");
                    var commands_table = new Table();

                    commands_table.AddColumn("[blue bold]Command name[/]");
                    commands_table.AddColumn(new TableColumn("[blue bold]Explanation[/]"));

                    commands_table.AddRow("[green]/exit[/]", "[grey]Closes all the connections, cleanly stops the server and exits.[/]");
                    commands_table.AddRow("[green]/quit[/]", "[grey]Same as[/] [green italic]/exit[/]");
                    commands_table.AddRow("[green]/stop[/]", "[grey]Same as[/] [green italic]/exit[/]");
                    commands_table.AddRow("[green]/ban[/]", "[grey]Bans an client. Usage:[/] [green italic]/ban <ip-address>[/]");
                    commands_table.AddRow("[green]/unban[/]", "[grey]Unbans an client. Usage:[/] [green italic]/unban <ip-address>[/]");
                    commands_table.AddRow("[green]/banlist[/]", "[grey]Lists the banned IPs in an table.[/]");
                    commands_table.AddRow("[green]/timeout[/]", "[grey]Shuts down the server after a timeout. Usage:[/] [green italic]/timeout <seconds>[/]");

                    AnsiConsole.Write(commands_table);
                } else if(string.IsNullOrEmpty(command)){
                    continue;
                } else {
                    broadcast_message($"[red bold]SERVER =>[/] {command}", null);
                }

                command = "";
            }
        }

        private static void handle_client(object clientObj)
        {
            if(clientObj == null)
                return;
            
            TcpClient client = (TcpClient)clientObj;
            NetworkStream? stream = null;
            string clientName = "<unknown>";

            try
            {
                stream = client.GetStream();
                byte[] buffer = new byte[1024];
                byte[] name_buffer = new byte[20]; // Limit only 20 char to prevent name exploit.

                int bytesRead;

                // Get the client's name
                bytesRead = stream.Read(name_buffer, 0, name_buffer.Length);
                if (bytesRead > 0)
                {
                    clientName = Encoding.ASCII.GetString(name_buffer, 0, bytesRead).Trim();
                    broadcast_message($"[yellow bold]{clientName} has joined the chat.[/]", client);
                    AnsiConsole.MarkupLine($"[grey]{client.Client.RemoteEndPoint} => {clientName}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"Client [grey]{client.Client.RemoteEndPoint}[/] [red]disconnected[/] before sending name.");
                    return;
                }

                Array.Clear(buffer, 0, buffer.Length);
                Array.Clear(name_buffer, 0, name_buffer.Length);

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
                            send_message($"There are [blue]{clients.Count}[/] member(s) [green]online[/].", client);

                        } else {
                            broadcast_message($"[grey]{clientName} =>[/] {message}", client);
                        }

                        if(msgs_per_unit.ContainsKey(client)){
                            msgs_per_unit[client]++;
                        } else {
                            msgs_per_unit[client] = 1;
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
                try{
                    AnsiConsole.MarkupLine($"[gray]Client disconnected: {clientName} ({client.Client.RemoteEndPoint})[/]");
                } catch {
                    AnsiConsole.MarkupLine($"[gray]Client disconnected: {clientName} (unknown end point)[/]");
                }
                stream?.Close();
                client?.Close();
            }
        }

        private static void send_message(string message, TcpClient target_client){
            if(target_client == null){
                print_error("Target client is null.");
                return;
            }

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
                target_client.Close();
                lock (clientsLock)
                {
                    clients.Remove(target_client);
                }
            }
        }

        private static void broadcast_message(string message, TcpClient sender_client)
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
                            client.Close();
                            lock (clientsLock)
                            {
                                clients.Remove(client);
                            }
                        }
                    }
                }
            }
        }

        private static void spam_protection(object clientObj){
            if(clientObj == null)
                return;
            
            TcpClient client = (TcpClient)clientObj;

            Thread.Sleep(5000); // 5 seconds.

            try{
                if(!msgs_per_unit.ContainsKey(client)){
                    msgs_per_unit[client] = 0;
                }

                if(msgs_per_unit[client] >= 4){ // 4 messages in 5 seconds.
                    send_message("[red]Kicked for spamming. Get out and touch grass.[/]", client);
                    AnsiConsole.MarkupLine($"[red]Kicked[/] [grey]{client.Client.RemoteEndPoint}[/] for spamming.");
                    client.Close();
                    lock (clientsLock)
                    {
                        clients.Remove(client);
                    }
                    msgs_per_unit.Remove(client);
                } else {
                    msgs_per_unit[client] = 0;
                }

            } catch (Exception ex){
                print_error($"Error in spam protection thread: {ex.Message}");
            }

            spam_protection(client);
        }

        private static void ban_ip(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
            {
                print_error("IP address is null or empty.");
                return;
            }

            lock (clientsLock)
            {
                banned_ips.Add(ipAddress);
                foreach (var client in clients.ToList())
                {
                    if (client.Client.RemoteEndPoint is IPEndPoint endPoint && endPoint.Address.ToString() == ipAddress)
                    {
                        send_message("[red]You have been banned from the server.[/]", client);
                        client.Close();
                        clients.Remove(client);
                        AnsiConsole.MarkupLine($"[red]Banned and disconnected client:[/] [grey]{ipAddress}[/]");
                    }
                }
            }
        }

        private static bool is_ip_banned(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
            {
                print_error("IP address is null or empty.");
                return false;
            }

            if(ipAddress.Contains(":"))  // Remove port if present
            {
                return banned_ips.Contains(ipAddress.Split(':')[0]);
            } else {
                return banned_ips.Contains(ipAddress);
            }
        }

        private static void list_banned_ips()
        {
            if (banned_ips.Count == 0)
            {
            AnsiConsole.MarkupLine("[green]No IPs are currently banned.[/]");
            return;
            }

            var table = new Table();
            table.AddColumn("[blue bold]Banned IPs[/]");

            foreach (var ip in banned_ips)
            {
            table.AddRow(ip);
            }

            AnsiConsole.Write(table);
        }
    }
}