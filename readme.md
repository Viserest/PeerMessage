**Peer** { <- Created from cli args or received from other peer, Created using Ip Address and Port maybe also PublicKey
    NetworkRule {
        Ip Address
        Port
    }
    TcpClient <- If null, must be created; If created, must be connected to (checked on sending function)
    PublicKey <- If null, must send authorization handshake
}

**Packet** {
    AuthHandshake
    Ping
    Message(String message)
    PeerSync(List<Peer>)
}

**Commands** <- type commands using `/`
    peer                **=== PEERLIST SUB-COMMANDS**
        auto            Toggle automatic peerlist syncing (using sync delay)
        sd|syncdelay    No args: Get, 1 arg: Set; *Minutes/Sync Request*
        max             No args: Get, 1 arg: Set; *Peerlist maximum allowed*
        list            List peerlist and their status; */peer defaults here*
        reload          Reload peerlist
        add             Add peer with IpAddress:Port@PublicKey; PublicKey is optional
        del|rm          Remove peer using PeerID
    block               **=== BLOCKLIST SUB-COMMANDS**
        auto            Toggle automatic blocking (using rate limiting)
        rl|ratelimit    No args: Get, 1 arg: Set; *Maximum Requests/Minute*
        list            List blocklist; */block defaults here*
        clear           Clear blocklist; *Cannot recover*
        add             Add blocked ip with Ip Address
        del|rm          Remove blocked ip with Ip Address
                        **=== MISC**
    w|whisper           Whisper to peer using PeerID `/whisper PeerID TEXT`
    h|help              Print this help
    quit|exit           Exit the program

**Self**
    List<Peer> peerlist;
    List<IPAddress> blocklist;

**Task**
    Console -> On newline:  text is sent to each peer `: TEXT`
    Peer1   -> On received: text is printed to console `PeerID[0:10]> TEXT`
    Peer2   -> *
    PeerN   -> *
