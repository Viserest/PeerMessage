
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

class Peer
{
    public IPAddress ip_address;
    public UInt16 port;
    private String? public_key = null;
    private String? peer_id = null;
    private TcpClient? current_client = null;
    private NetworkStream? stream = null;
    public Peer(String ip_port_publickey)
    {
        if (String.IsNullOrWhiteSpace(ip_port_publickey))
        {
            Console.WriteLine("! Must be written in the form: 'IpAddress:Port@PublicKey'");
            throw new Exception();
        }

        // Split the argument by colon
        String[] parts_colon = ip_port_publickey.Split(":");
        if (parts_colon.Length != 2)
        {
            Console.WriteLine("! Must be written in the form: 'IpAddress:Port@PublicKey'");
            throw new Exception();
        }

        // Split the argument by at symbol
        String[] parts_at = parts_colon[1].Split("@");

        // First argument, if valid is the ip address
        try
        {
            this.ip_address = IPAddress.Parse(parts_colon[0]);
        }
        // Will be FormatException
        catch
        {
            Console.WriteLine("! Invalid Ip Address");
            throw new Exception();
        }
        
        // Second argument, if valid uint16 is the port
        try
        {
            this.port = UInt16.Parse(parts_at[0]);
        }
        // Either FormatException or OverflowException
        // Either way, its an invalid port
        catch
        {
            Console.WriteLine("! Invalid Port");
            throw new Exception();
        }

        // Third argument, if provided is PublicKey
        if (parts_at.Length == 2) {
            this.public_key = parts_at[1];
        }
    }
    public void SetClient(TcpClient client)
    {
        this.current_client = client;
    }
    public bool MatchesIPAddress(IPAddress address)
    {
        return this.ip_address.Equals(address);
    }
    public bool MatchesPeerId(String peer_id)
    {
        if (this.peer_id == null) return false;
        return this.peer_id.Equals(peer_id);
    }
    public void Print()
    {
        if (this.peer_id == null) {
            Console.WriteLine("{1}:{2}@UNKNOWN", this.ip_address, this.port);
        }
        else
        {
            Console.WriteLine("{1}:{2}@{3}", this.ip_address, this.port, this.peer_id);
        }
    }
    public async Task ConnectAsync()
    {
        if (this.current_client == null)
        {
            this.current_client = new();
        }

        await this.current_client.ConnectAsync(this.ip_address, this.port);
        Console.WriteLine("! Connected to {0}:{1}", this.ip_address, this.port);

        this.stream = this.current_client.GetStream();
    }
    public async Task ReceiveMessageAsync(CancellationToken token)
    {
        if (this.stream == null)
        {
            await ConnectAsync();
        }
        if (this.stream == null) return;

        StreamReader reader = new(
            this.stream,
            Encoding.UTF8,
            leaveOpen: true
        );

        try
        {
            while (!token.IsCancellationRequested)
            {
                String? message = await reader.ReadLineAsync(token);
                if (message == null) break;

                Console.WriteLine("\n{0}:{1}]: {2}", this.ip_address, this.port, message);
                Console.Write("> ");
            }
        }
        catch (IOException) {}
    }
    public async Task SendMessageAsync(String message)
    {
        if (this.stream == null)
        {
            await ConnectAsync();
        }
        if (this.stream == null) return;

        StreamWriter writer = new(
            this.stream,
            Encoding.UTF8,
            leaveOpen: true)
        {
            AutoFlush = true
        };

        await writer.WriteLineAsync(message);
    }
}

class Startup {
    private TcpListener?            server = null;
    private List<Peer>              peerlist = new();
    private UInt32                  peerlist_max = 100;
    private bool                    peerlist_enabled = true;
    // Minutes between Sync Request
    private UInt32                  peerlist_syncdelay = 10;
    private List<IPAddress>         blocklist = new();
    private bool                    blocklist_enabled = true;
    // External Requests/Minute
    private UInt32                  blocklist_ratelimit = 500;
    private Char                    cmd_prefix = '/';
    private CancellationTokenSource cancellation = new();
    static void ShowCliHelp()
    {
        Console.WriteLine("Simple C# Server/Client Program");
        Console.WriteLine("");
        Console.WriteLine("Usage: dotnet run [OPTIONS]");
        Console.WriteLine("");
        Console.WriteLine("OPTIONS");
        Console.WriteLine("    --port Port                        Listening port for peers");
        Console.WriteLine("    --peer IpAddress:Port@PublicKey    Add peer to peerlist; optional PublicKey");
        Console.WriteLine("    --block IpAddress                  Add ip to blocklist");
        Console.WriteLine("    --cmd-prefix Char                  Character which starts commands");
        Console.WriteLine("    --help                             Show this help page");
        Console.WriteLine("");
    }
    // Returning true keeps running but false exits program with 1
    bool HandleArguments(String[] args, int i)
    {
        switch (args[i])
        {
            case "--port": {
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine("! Port option must include Port");
                    return false;
                }
                server = new(IPAddress.Any, UInt16.Parse(args[i + 1]));
                break;
            }
            case "--peer": {
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine("! Peer option must include IpAddress, Port and optional PublicKey");
                    return false;
                }
                peerlist.Add(new Peer(args[i + 1]));
                break;
            }
            case "--block": {
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine("! Block option must include IpAddress");
                    return false;
                }
                blocklist.Add(IPAddress.Parse(args[i + 1]));
                break;
            }
            case "--cmd-prefix":
            {
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine("! Command prefix option must include Char");
                    return false;
                }
                cmd_prefix = Char.Parse(args[i + 1]);
                break;
            }
            case "--help": {
                ShowCliHelp();
                return false;
            }
        }
        return true;
    }
    static void ShowCmdHelp()
    {
        Console.WriteLine("");
        Console.WriteLine("COMMANDS");
        Console.WriteLine("peer                === PEERLIST SUB-COMMANDS");
        Console.WriteLine("    auto            Toggle automatic peerlist syncing (using sync delay)");
        Console.WriteLine("    sd|syncdelay    No args: GET, 1 arg: SET; Minutes until sync request");
        Console.WriteLine("    max             No args: GET, 1 arg: SET; Maximum peerlist size");
        Console.WriteLine("    list            List peerlist and their status");
        Console.WriteLine("    reload          Reload peerlist");
        Console.WriteLine("    add             Add peer with IpAddress:Port@PublicKey; optional PublicKey");
        Console.WriteLine("    del|rm          Remove peer using PeerID");
        Console.WriteLine("");
        Console.WriteLine("block               === BLOCKLIST SUB-COMMAND");
        Console.WriteLine("    auto            Toggle automatic blocking (using rate limiting)");
        Console.WriteLine("    rl|ratelimit    No args: GET, 1 arg: SET; Maximum requests/minute");
        Console.WriteLine("    list            List blocklist");
        Console.WriteLine("    clear           Clear blocklist");
        Console.WriteLine("    add             Add ip to blocklist");
        Console.WriteLine("    del|rm          Remove ip from blocklist");
        Console.WriteLine("");
        Console.WriteLine("w|whisper           Whisper to peer using PeerID");
        Console.WriteLine("h|help              Print this help");
        Console.WriteLine("quit|exit           Exit the program");
        Console.WriteLine("");
    }
    async Task HandleCommands(String[] cmds)
    {
        Console.WriteLine("ARGS");
        foreach (String cmd in cmds)
        {
            Console.WriteLine("A: {0}", cmd);
        }
        Console.WriteLine("");
        
        int i = 0;
        switch (cmds[i++])
        {
            case "peer": {
                if (i + 1 >= cmds.Length)
                {
                    Console.WriteLine("! Peer command requires sub-command");
                    return;
                }
                switch (cmds[i++])
                {
                    case "auto": {
                        if (this.peerlist_enabled)
                        {
                            this.peerlist_enabled = false;
                            Console.WriteLine("! Peerlist set to manual");
                        }
                        else
                        {
                            this.peerlist_enabled = true;
                            Console.WriteLine("! Peerlist set to auto");
                        }
                        break;
                    }
                    case "sd":
                    case "syncdelay": {
                        if (i + 1 >= cmds.Length)
                        {
                            // GET
                            Console.WriteLine("! Sync delay is set to: {0}", this.peerlist_syncdelay);
                        }
                        else
                        {
                            // SET
                            try
                            {
                                this.peerlist_syncdelay = UInt32.Parse(cmds[i++]);
                            } catch {}
                            Console.WriteLine("! Sync delay is set to: {0}", this.peerlist_syncdelay);
                        }
                        break;
                    }
                    case "max": {
                        if (i + 1 >= cmds.Length)
                        {
                            // GET
                            Console.WriteLine("! Peerlist max is set to: {0}", this.peerlist_max);
                        }
                        else
                        {
                            // SET
                            try
                            {
                                this.peerlist_max = UInt32.Parse(cmds[i++]);
                            } catch {}
                            Console.WriteLine("! Peerlist max is set to: {0}", this.peerlist_max);
                        }
                        break;
                    }
                    case "list": {
                        int j = 0;
                        foreach (Peer peer in this.peerlist)
                        {
                            Console.Write("{0}. ", j);
                            peer.Print();
                            j++;
                        }
                        break;
                    }
                    case "reload": {
                        foreach (Peer peer in this.peerlist)
                        {
                            await peer.ConnectAsync();
                        }
                        break;
                    }
                    case "add": {
                        if (i + 1 >= cmds.Length)
                        {
                            Console.WriteLine("! Peer option must include IpAddress, Port and optional PublicKey");
                            return;
                        }
                        this.peerlist.Add(new Peer(cmds[i + 1]));
                        break;
                    }
                    case "del":
                    case "rm": {
                        if (i + 1 >= cmds.Length)
                        {
                            Console.WriteLine("! Missing PeerID for removal");
                            return;
                        }
                        int removed = this.peerlist.RemoveAll(peer => peer.MatchesPeerId(cmds[i++]));
                        Console.WriteLine("! Removed {0} peers", removed);
                        break;
                    }
                    default: {
                        Console.WriteLine("! Sub-Command not found");
                        break;
                    }
                }
                break;
            }
            case "block": {
                if (i + 1 >= cmds.Length)
                {
                    Console.WriteLine("! Block command requires sub-command");
                    return;
                }
                switch (cmds[i++])
                {
                    case "auto": {
                        if (this.blocklist_enabled)
                        {
                            this.blocklist_enabled = false;
                            Console.WriteLine("! Blocklist set to manual");
                        }
                        else
                        {
                            this.blocklist_enabled = true;
                            Console.WriteLine("! Blocklist set to auto");
                        }
                        break;
                    }
                    case "rl":
                    case "ratelimit": {
                        if (i + 1 >= cmds.Length)
                        {
                            // GET
                            Console.WriteLine("! Ratelimit is set to: {0}", this.blocklist_ratelimit);
                        }
                        else
                        {
                            // SET
                            try
                            {
                                this.blocklist_ratelimit = UInt32.Parse(cmds[i++]);
                            } catch {}
                            Console.WriteLine("! Ratelimit is set to: {0}", this.blocklist_ratelimit);
                        }
                        break;
                    }
                    case "list": {
                        int j = 0;
                        foreach (IPAddress ip_address in this.blocklist)
                        {
                            Console.WriteLine("{0}. {1}", j, ip_address);
                            j++;
                        }
                        break;
                    }
                    case "clear": {
                        this.blocklist.Clear();
                        break;
                    }
                    case "add": {
                        if (i + 1 >= cmds.Length)
                        {
                            Console.WriteLine("! Block option must include IpAddress");
                            return;
                        }
                        this.blocklist.Add(IPAddress.Parse(cmds[i + 1]));
                        break;
                    }
                    case "del":
                    case "rm": {
                        if (i + 1 >= cmds.Length)
                        {
                            Console.WriteLine("! Missing IpAddress for removal");
                            return;
                        }
                        int removed = this.blocklist.RemoveAll(ip => ip.Equals(cmds[i++]));
                        Console.WriteLine("! Removed {0} blocked IpAddresses", removed);
                        break;
                    }
                    default: {
                        Console.WriteLine("! Sub-Command not found");
                        break;
                    }
                }
                break;
            }
            case "w":
            case "whisper": {
                if (i + 2 >= cmds.Length)
                {
                    Console.WriteLine("! Whisper command requires PeerID and Message");
                    return;
                }
                String peer_id = cmds[i++];
                String message = String.Join(' ', cmds, i++, cmds.Length);
                break;
            }
            case "h":
            case "help": {
                ShowCmdHelp();
                break;
            }
            case "quit":
            case "exit": {
                cancellation.Cancel();
                break;
            }
            default: {
                Console.WriteLine("! Command not found");
                break;
            }
        }
    }
    static void UpdateInputLine(bool is_command, StringBuilder sb)
    {
        Console.Write("\r{0} {1}", is_command ? '/' : ':', sb.ToString());
    }
    async Task HandleInput()
    {
        CancellationToken token = cancellation.Token;

        // Reuse for each typed line
        bool is_command = false;
        ConsoleKey key;
        char key_char;
        StringBuilder sb = new();

        Console.Write("\r: ");
        while (!token.IsCancellationRequested)
        {
            // Colon means your typing a message
            // Slash means your typing a command
            // 
            // If at the start (no text is typed yet), you type a slash:
            //     Converts to command prompt
            //     Puts line into string command
            // Else:
            //     Keeps as message prompt
            //     Puts line into string message

            // Get pressed key
            ConsoleKeyInfo key_info = Console.ReadKey(false);
            key = key_info.Key;
            key_char = key_info.KeyChar;

            // Handle which key was pressed
            // Switching to command prompt
            if (key_char == cmd_prefix && sb.Length < 1 && !is_command)
            {
                if (is_command) continue;
                // Can only happen when string is empty and not currently a command
                is_command = true;
                Console.Write("\r/  \b");
            }
            // Switching back to message prompt
            else if (key == ConsoleKey.Escape && is_command)
            {
                is_command = false;
                sb.Clear();
                Console.Write("\r: ");
            }
            // Make backspace
            else if (key == ConsoleKey.Backspace)
            {
                if (sb.Length < 1) continue;
                sb.Remove(sb.Length - 1, 1);
                Console.Write("\b ");
                UpdateInputLine(is_command, sb);
            }
            // Run command or send message
            else if (key == ConsoleKey.Enter)
            {
                // Empty string
                if (sb.Length < 1) {
                    Console.Write("\r: ");
                    continue;
                }

                if (is_command)
                {
                    Console.WriteLine("'/{0}'", sb.ToString());
                    await HandleCommands(sb.ToString().Split(' '));
                }
                else
                {
                    // If you have peers then send to everyone
                    if (peerlist.Count != 0)
                    {
                        foreach (Peer peer in peerlist)
                        {
                            await peer.SendMessageAsync(sb.ToString());
                        }
                    }

                    // Print message and maybe error message
                    Console.WriteLine("< {0}", sb.ToString());
                    if (peerlist.Count == 0)
                    {
                        Console.WriteLine("! No peers have been created");
                    }
                }
                is_command = false;
                sb.Clear();
                Console.Write("\r: ");
            }
            // All other keys
            else
            {
                sb.Append(key_char);
                UpdateInputLine(is_command, sb);
            }
        }
    }
    async Task<bool> HandleServerSetup()
    {
        // Port option wasnt called
        if (server == null)
        {
            Console.WriteLine("! Port option must be provided");
            return false;
        }

        // Start server
        IPEndPoint endpoint = (IPEndPoint)server.LocalEndpoint;
        Console.WriteLine("! Listening on 0.0.0.0:{0}", endpoint.Port);
        server.Start();

        return true;
    }
    async Task<bool> HandleServerAcceptClient() {
        // This will never happen, HandleServerSetup will resolve a null value
        if (server == null) return false;

        TcpClient client = await server.AcceptTcpClientAsync();
        IPEndPoint? endpoint = (IPEndPoint?)client.Client.RemoteEndPoint;
        if (endpoint == null)
        {
            client.Dispose();
            return false;
        }

        Peer? peer = peerlist.FirstOrDefault(p => p.MatchesIPAddress(endpoint.Address));
        if (peer == null)
        {
            Console.WriteLine("! Rejected connection from {0}", endpoint.Address);
            client.Dispose();
            return false;
        }

        // Create a task per peer connection
        _ = Task.Run(async () => {
            peer.SetClient(client);
            await peer.ReceiveMessageAsync(cancellation.Token);
        });
        return true;
    }
    static async Task<int> Main(String[] args)
    {
        Startup startup = new();

        // Handle arguments
        for (int i = 0; i < args.Length; i++) {
            if (!startup.HandleArguments(args, i)) return 1;
        }

        // Initial peer connections
        foreach (Peer peer in startup.peerlist)
        {
            Console.WriteLine("! Connecting to {0}:{1}", peer.ip_address, peer.port);

            // Create a task per peer connection
            _ = Task.Run(async () => {
                await peer.ReceiveMessageAsync(startup.cancellation.Token);
            });
        }

        // Handle server
        if (!await startup.HandleServerSetup())
        {
            Console.WriteLine("! Error setting up server");
            return 1;
        }
        _ = Task.Run(async () => {
            while (!startup.cancellation.Token.IsCancellationRequested) {
                if (!await startup.HandleServerAcceptClient())
                {
                    Console.WriteLine("! Error handling new peer");
                    continue;
                }
            }
        });
        
        // Gather console message and send to each peer
        await startup.HandleInput();

        return 0;
    }
}