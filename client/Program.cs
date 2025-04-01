using System.Net.Sockets;
using System.Text;

using Spectre.Console;

namespace ChatClient
{
    class prad_buffer {
        private char[] buffer = new char[1024];

        public int buffer_index = 0;

        public int length = 0;

        public void add_char(char c){
            if(buffer_index >= buffer.Length)
                return;

            buffer[buffer_index] = c;
            buffer_index++;
            length++;
        }

        public void clear_buffer(){
            for(int i = 0; i < buffer.Length; i++){
                buffer[i] = '\0';
            }

            buffer_index = 0;
            length = 0;
        }

        public void get_input(){
            ConsoleKeyInfo current;

            while (true)
            {
                if(!Console.KeyAvailable) continue;

                current = Console.ReadKey(true);

                if (current.Key == ConsoleKey.Enter)
                {
                    if(length != 0)
                        Console.WriteLine();

                    for(int i=0; i<length;i++){
                        if(buffer[i] == '\0'){
                            length = i;
                            break;
                        }
                    }
                    
                    break;
                }
                else if (current.Key == ConsoleKey.Backspace)
                {
                    if (buffer_index > 0)
                    {
                        buffer_index--;
                        length--;
                        Console.Write("\b \b");
                    }
                }
                else if(current.Key == ConsoleKey.LeftArrow){
                    if(Console.CursorLeft > 0){
                        Console.CursorLeft--;
                        buffer_index--;
                    }
                }
                else if(current.Key == ConsoleKey.RightArrow){
                    if(Console.CursorLeft < length){
                        Console.CursorLeft++;
                        buffer_index++;
                    }
                }
                else if(current.Key == ConsoleKey.Home){
                    Console.Write("\r");
                    buffer_index = 0;
                }
                else if(current.Key == ConsoleKey.End){
                    Console.CursorLeft = length;
                    buffer_index = length;
                }
                else
                {
                    add_char(current.KeyChar);
                    Console.Write(current.KeyChar);
                }
            }
        }

        public char[] get_buffer(){
            return buffer;
        }

        public string get_buffer_as_string(){
            return new string(buffer, 0, length);
        }
    }

    class Client
    {
        private static TcpClient? client;
        private static NetworkStream? stream;

        private static string username = "<not_set>";

        static prad_buffer input_buffer = new prad_buffer();

        static void print_error(string message){
            if(Console.CursorLeft > 0){
                Console.WriteLine();
            }

            AnsiConsole.MarkupLine($"[bold red]Error :[/] [grey]{message.Replace("[", "[[").Replace("]", "]]")}[/]");
        }

        static void print_info(string message){
            if(Console.CursorLeft > 0){
                Console.WriteLine();
            }

            AnsiConsole.MarkupLine($"[bold blue]Info :[/] [grey]{message.Replace("[", "[[").Replace("]", "]]")}[/]");
        }

        static void Main(string[] args)
        {
            Console.Clear();

            Console.Title = "Platter - Client";

            var intro_table = new Table();

            intro_table.AddColumn("[blue bold]Introduction[/]");

            intro_table.AddRow("[green]Welcome[/] to [red]client-side[/] application of platter!");
            intro_table.AddRow("This is a [red]simple chat application[/] that allows multiple clients to connect and communicate with each other.\n");
            intro_table.AddRow("Check out GitHub repository → [blue]https://github.com/pradosh-arduino/platter[/]");

            AnsiConsole.Write(intro_table);

            try
            {
                AnsiConsole.Write(new Rule("[bold blue]Server information.[/]").LeftJustified());

                string ip_address = AnsiConsole.Prompt(new TextPrompt<string>("Enter [red]server[/] IP Address →"));
                int ip_port = AnsiConsole.Prompt(new TextPrompt<int>("Enter [red]server[/] port →"));

                client = new TcpClient(ip_address, ip_port); // Connect to the server

                stream = client.GetStream();

                username = AnsiConsole.Prompt(new TextPrompt<string>("Enter [red]server[/] [green]public[/] username →"));
                if(username.Length < 3 || username.Length > 20)
                {
                    print_error("Username must be between 3 and 20 characters.");
                    Environment.Exit(1);
                }
                byte[] username_encoded = Encoding.ASCII.GetBytes(username); // Username must be in ASCII only.
                stream.Write(username_encoded, 0, username_encoded.Length);

                Thread receiveThread = new Thread(receive_messages);
                receiveThread.Start();

                Console.Clear();
                AnsiConsole.Write(new Rule("[bold yellow]Connected successfully![/]").LeftJustified());

                send_messages();
            }
            catch (Exception ex)
            {
                print_error(ex.Message);
            }
            finally
            {
                stream?.Close();
                client?.Close();
            }
        }

        private static void send_messages(){
            string? message;

            if(stream == null)
                return;

            input_buffer.clear_buffer(); // For safety reasons.

            while (true){

                // START - Using custom input buffer
                input_buffer.get_input();

                message = input_buffer.get_buffer_as_string().Trim();

                input_buffer.clear_buffer();
                // END - Using custom input buffer
                
                if (string.IsNullOrEmpty(message))
                    continue;

                if (message == "/ping")
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    
                    byte[] buffer = new byte[1024];
                    int bytesRead = 0;
                    byte[] data = Encoding.Unicode.GetBytes(message);

                    stream.Write(data, 0, data.Length);

                    bytesRead = stream.Read(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        string response = Encoding.Unicode.GetString(buffer, 0, bytesRead);

                        if (response == "[green]Pong![/]")
                        {
                            stopwatch.Stop();
                            AnsiConsole.MarkupLine($"[bold green]Pong received![/] Response time: [yellow]{stopwatch.ElapsedMilliseconds} ms[/]");
                            var chart = new BarChart()
                                .Label("[bold blue]Ping Response Time[/]")
                                .AddItem("Ping", stopwatch.ElapsedMilliseconds, 
                                    stopwatch.ElapsedMilliseconds > 200 ? Color.Red : 
                                    stopwatch.ElapsedMilliseconds > 100 ? Color.Yellow : 
                                    stopwatch.ElapsedMilliseconds > 050 ? Color.Green :
                                    Color.Blue);

                            AnsiConsole.Write(chart);
                        }
                        else
                        {
                            print_error("Unexpected response from server.");
                        }
                    } else {
                        print_error("No response from server. Something went wrong");
                    }
                }
                else if(message == "/exit" || message == "/quit" || message == "/leave")
                {
                    print_info("Disconnecting from server...");
                    break;
                }
                else if(message == "/clear")
                {
                    Console.Clear();
                    AnsiConsole.Write(new Rule("[bold yellow]Connected successfully![/]").LeftJustified());
                }
                else if(message == "/help"){
                    help_table();
                } else if(message == "/members"){

                    byte[] data = Encoding.Unicode.GetBytes(message);
                    stream.Write(data, 0, data.Length);

                    Thread.Sleep(300); //ms, wait for the response.

                } else if(message.StartsWith("/"))
                {
                    AnsiConsole.MarkupLine($"[red]Unknown command.[/] [grey italic](Only you can see this message, other members cannot.)[/]");
                    help_table();
                } else
                {
                    byte[] data = Encoding.Unicode.GetBytes(message);
                    stream.Write(data, 0, data.Length);
                }

                message = "";
            }
        }

        private static void receive_messages()
        {
            if(stream == null)
                return;

            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead;
                while (true)
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        string message = Encoding.Unicode.GetString(buffer, 0, bytesRead);

                        if(message == "[green]Pong![/]")
                        {
                            // Ignore the pong message.
                            continue;
                        }   

                        if(input_buffer.length > 0){
                            Console.Write("\r"); // carriage returns

                            int prev_top = Console.CursorTop;
                            int prev_left = Console.CursorLeft;

                            for(int i=0; i<input_buffer.length; i++){
                                Console.Write(' ');
                            }

                            Console.CursorTop = prev_top;
                            Console.CursorLeft = prev_left;
                        }

                        AnsiConsole.MarkupLine(message);

                        // print the buffer back below
                        if(input_buffer.length > 0){
                            Console.Write(input_buffer.get_buffer_as_string());
                        }
                    }
                    else
                    {
                        // Server disconnected
                        print_info("Disconnected from server.");
                        Environment.Exit(1);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                print_error($"Not receiving messages: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void help_table(){
            var commands_table = new Table();

            commands_table.AddColumn("[blue bold]Command name[/]");
            commands_table.AddColumn(new TableColumn("[blue bold]Explanation[/]"));

            commands_table.AddRow("[green]/exit[/]", "[grey]Closes the connection with server, cleanly disconnects from the server and exits.[/]");
            commands_table.AddRow("[green]/quit[/]", "[grey]Same as[/] [green italic]/exit[/]");
            commands_table.AddRow("[green]/leave[/]", "[grey]Same as[/] [green italic]/exit[/]");
            commands_table.AddRow("[green]/ping[/]", "[grey]Get the ping in milliseconds.[/]");
            commands_table.AddRow("[green]/clear[/]", "[grey]Clears the console screen.[/]");
            commands_table.AddRow("[green]/members[/]", "[grey]Shows list of members who are currently online.[/]");
            commands_table.AddRow("[green]/help[/]", "[grey]Shows this help message.[/]");

            AnsiConsole.Write(commands_table);
        }
    }
}