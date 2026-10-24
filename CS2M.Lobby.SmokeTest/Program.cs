// Usage: dotnet run -- <lobby host> [udp port] [http port]
// Exits 0 when every check passes.
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CS2M.Commands.ApiServer;
using LiteNetLib;
using MessagePack;
using MessagePack.Attributeless;
using MessagePack.Resolvers;

string lobbyHost = args.Length > 0 ? args[0] : "127.0.0.1";
int udpPort = args.Length > 1 ? int.Parse(args[1]) : 4242;
int httpPort = args.Length > 2 ? int.Parse(args[2]) : 8080;
IPAddress lobbyAddress = Dns.GetHostAddresses(lobbyHost).First(a => a.AddressFamily == AddressFamily.InterNetwork);
var lobby = new IPEndPoint(lobbyAddress, udpPort);
var http = new HttpClient { BaseAddress = new Uri($"http://{lobbyHost}:{httpPort}/") };
int failures = 0;

// The mod's model: ApiCommand.RefreshModel, ProtocolOrder
var builder = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance).Configure();
foreach (Type type in new[]
         {
             typeof(PortCheckRequestCommand), typeof(PortCheckResultCommand), typeof(ServerRegistrationCommand),
             typeof(SessionInfoCommand), typeof(ServerUnregisterCommand)
         })
{
    builder.SubType(typeof(ApiCommandBase), type);
}

MessagePackSerializerOptions model = builder.Build();
byte[] Serialize(ApiCommandBase command) => MessagePackSerializer.Serialize(command, model);

void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}");
    if (!ok) failures++;
}

void Pump(int ms, params NetManager[] managers)
{
    for (int i = 0; i < ms / 10; i++)
    {
        foreach (NetManager m in managers)
        {
            m.PollEvents();
            m.NatPunchModule.PollEvents();
        }

        Thread.Sleep(10);
    }
}

async Task<JsonElement> Sessions() =>
    JsonDocument.Parse(await http.GetStringAsync("sessions")).RootElement;

// Host: like NetworkManager.StartServer
var hostListener = new EventBasedNetListener();
PortCheckResultCommand portResult = null;
hostListener.ConnectionRequestEvent += r => r.AcceptIfKey("CSM");
hostListener.NetworkReceiveUnconnectedEvent += (ep, reader, _) =>
    portResult = MessagePackSerializer.Deserialize<ApiCommandBase>(reader.GetRemainingBytes(), model) as PortCheckResultCommand;
bool hostSawPeer = false;
hostListener.PeerConnectedEvent += _ => hostSawPeer = true;
var host = new NetManager(hostListener) { NatPunchEnabled = true, UnconnectedMessagesEnabled = true };
host.Start(0);
host.NatPunchModule.Init(new EventBasedNatPunchListener());
string localIp = NetUtils.GetLocalIp(LocalAddrType.IPv4);

Check((await http.GetAsync("health")).IsSuccessStatusCode, "GET /health");

const string token = "SMOKE12345";
host.SendUnconnectedMessage(Serialize(new SessionInfoCommand
{
    Token = token, LocalIp = localIp, LocalPort = host.LocalPort, CityName = "Smokeville", HostName = "tester",
    PlayerCount = 1, MaxPlayers = 0, HasPassword = true, GameVersion = "1.2.3f1", ModVersion = "0.3.0.0"
}), lobby);
Pump(300, host);
string raw = await http.GetStringAsync("sessions");
JsonElement listed = (await Sessions()).EnumerateArray().FirstOrDefault(s => s.GetProperty("token").GetString() == token);
Check(listed.ValueKind == JsonValueKind.Object && listed.GetProperty("name").GetString() == "Smokeville" &&
      listed.GetProperty("host").GetString() == "tester" && listed.GetProperty("hasPassword").GetBoolean() &&
      listed.GetProperty("players").GetInt32() == 1, "SessionInfoCommand listed with its details");
Check(!raw.Contains(localIp) && !raw.Contains("\"ip\"", StringComparison.OrdinalIgnoreCase), "no addresses in /sessions");
Console.WriteLine("  /sessions: " + raw);

// Port check: open (the host rejects the probe's key), then a closed port
host.SendUnconnectedMessage(Serialize(new PortCheckRequestCommand { Port = host.LocalPort }), lobby);
for (int i = 0; i < 100 && portResult == null; i++) Pump(100, host);
Check(portResult?.State == PortCheckResult.Reachable, $"port check open port -> {portResult?.State}");
portResult = null;
host.SendUnconnectedMessage(Serialize(new PortCheckRequestCommand { Port = 1 }), lobby);
for (int i = 0; i < 100 && portResult == null; i++) Pump(100, host);
Check(portResult?.State == PortCheckResult.Unreachable, $"port check closed port -> {portResult?.State}");

// Joiner: like NetworkManager.SetupNatConnect + Connect
var joinListener = new EventBasedNetListener();
bool joined = false;
joinListener.PeerConnectedEvent += _ => joined = true;
var joiner = new NetManager(joinListener) { NatPunchEnabled = true, UnconnectedMessagesEnabled = true };
joiner.Start();
var natListener = new EventBasedNatPunchListener();
IPEndPoint punched = null;
natListener.NatIntroductionSuccess += (point, type, t) => punched ??= point;
joiner.NatPunchModule.Init(natListener);
joiner.NatPunchModule.SendNatIntroduceRequest(lobby, "token:" + token);
for (int i = 0; i < 50 && punched == null; i++) Pump(100, host, joiner);
Check(punched != null, "NAT introduction by token");
if (punched != null)
{
    joiner.Connect(punched, "CSM");
    for (int i = 0; i < 50 && !joined; i++) Pump(100, host, joiner);
    Check(joined && hostSawPeer, "joiner connected to the host");
}

// ip: form, with the port
punched = null;
var joiner2 = new NetManager(new EventBasedNetListener()) { NatPunchEnabled = true };
joiner2.Start();
var natListener2 = new EventBasedNatPunchListener();
natListener2.NatIntroductionSuccess += (point, type, t) => punched ??= point;
joiner2.NatPunchModule.Init(natListener2);
// what the lobby sees as the host's address is our own outgoing address towards it
using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
{
    probe.Connect(lobby);
    var self = (IPEndPoint)probe.LocalEndPoint!;
    joiner2.NatPunchModule.SendNatIntroduceRequest(lobby, "ip:" + new IPEndPoint(self.Address, host.LocalPort));
}
for (int i = 0; i < 50 && punched == null; i++) Pump(100, host, joiner2);
Check(punched != null, "NAT introduction by ip:address:port");

// Unknown token: no introduction
punched = null;
joiner2.NatPunchModule.SendNatIntroduceRequest(lobby, "token:NOSUCHTOKEN");
Pump(1500, host, joiner2);
Check(punched == null, "no introduction for an unknown token");

// The original registration command still works (listed without details)
host.SendUnconnectedMessage(Serialize(new ServerRegistrationCommand { Token = "LEGACY0001", LocalIp = localIp, LocalPort = host.LocalPort }), lobby);
Pump(300, host);
JsonElement legacy = (await Sessions()).EnumerateArray().FirstOrDefault(s => s.GetProperty("token").GetString() == "LEGACY0001");
Check(legacy.ValueKind == JsonValueKind.Object && legacy.GetProperty("name").ValueKind == JsonValueKind.Null,
    "ServerRegistrationCommand accepted");

// Another address can't take over a token: shown by sending from a second socket only if the address differs,
// so here just check unregistering removes both
host.SendUnconnectedMessage(Serialize(new ServerUnregisterCommand { Token = token }), lobby);
host.SendUnconnectedMessage(Serialize(new ServerUnregisterCommand { Token = "LEGACY0001" }), lobby);
Pump(300, host);
Check((await Sessions()).GetArrayLength() == 0, "ServerUnregisterCommand removes the sessions");

host.Stop();
joiner.Stop();
joiner2.Stop();
Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
