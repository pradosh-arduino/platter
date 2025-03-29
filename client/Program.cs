using System.Net.Sockets;
using System.Text;

using Spectre.Console;

namespace ChatClient
{
    class Client
    {
        static TcpClient? client;
        static NetworkStream? stream;

        static void print_error(string message){
            AnsiConsole.MarkupLine($"[bold red]Error :[/] [grey]{message.Replace("[", "[[").Replace("]", "]]")}[/]");
        }

        static void print_info(string message){
            AnsiConsole.MarkupLine($"[bold blue]Info :[/] [grey]{message.Replace("[", "[[").Replace("]", "]]")}[/]");
        }

        static void Main(string[] args)
        {
            Console.Clear();

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

                string username = AnsiConsole.Prompt(new TextPrompt<string>("Enter [red]server[/] [green]public[/] username →"));
                byte[] username_encoded = Encoding.ASCII.GetBytes(username);
                stream.Write(username_encoded, 0, username_encoded.Length);

                Thread receiveThread = new Thread(ReceiveMessages);
                receiveThread.Start();

                Console.Clear();
                AnsiConsole.Write(new Rule("[bold yellow]Connected successfully![/]").LeftJustified());

                string? message;

                while (true){
                    message = Console.ReadLine();

                    if (string.IsNullOrEmpty(message))
                        break;

                    if (message == "/ping")
                    {
                        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                        byte[] data = Encoding.ASCII.GetBytes(message);
                        stream.Write(data, 0, data.Length);

                        byte[] buffer = new byte[1024];
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                            if (response.Contains("Pong!"))
                            {
                                stopwatch.Stop();
                                AnsiConsole.MarkupLine($"[bold green]Pong received![/] Response time: [yellow]{stopwatch.ElapsedMilliseconds} ms[/]");
                            }
                            else
                            {
                                AnsiConsole.MarkupLine("[bold red]Unexpected response from server.[/]");
                            }
                        }
                    }
                    else if(message == "/exit" || message == "/quit" || message == "/leave")
                    {
                        print_info("Disconnecting from server...");
                        break;
                    }
                    else if(message.Trim().StartsWith("/"))
                    {
                        AnsiConsole.MarkupLine($"[red]Unknown command.[/] [grey italic](Only you can see this message, other members cannot.)[/]");
                        var commands_table = new Table();

                        commands_table.AddColumn("[blue bold]Command name[/]");
                        commands_table.AddColumn(new TableColumn("[blue bold]Explanation[/]"));

                        commands_table.AddRow("[green]/exit[/]", "[grey]Closes the connection with server, cleanly disconnects from the server and exits.[/]");
                        commands_table.AddRow("[green]/quit[/]", "[grey]Same as[/] [green italic]/exit[/]");
                        commands_table.AddRow("[green]/leave[/]", "[grey]Same as[/] [green italic]/exit[/]");
                        commands_table.AddRow("[green]/ping[/]", "[grey]Get the ping in milliseconds.[/]");

                        AnsiConsole.Write(commands_table);
                    }
                    else
                    {
                        AnsiConsole.Status().Start("Sending message...", ctx =>
                        {
                            byte[] data = Encoding.ASCII.GetBytes(message);
                            stream.Write(data, 0, data.Length);
                        });
                    }
                }
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

        static void ReceiveMessages()
        {
            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead;
                while (true)
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                        AnsiConsole.MarkupLine(message);
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
    }
}