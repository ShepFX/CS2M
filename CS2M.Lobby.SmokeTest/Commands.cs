// Copies of CS2M/Commands/ApiServer: the property names are what matters on the wire
namespace CS2M.Commands.ApiServer
{
    public abstract class ApiCommandBase
    {
    }

    public class PortCheckRequestCommand : ApiCommandBase
    {
        public int Port { get; set; }
    }

    public class PortCheckResultCommand : ApiCommandBase
    {
        public PortCheckResult State { get; set; }
        public string Message { get; set; }
    }

    public enum PortCheckResult
    {
        Reachable,
        Unreachable,
        Error
    }

    public class ServerRegistrationCommand : ApiCommandBase
    {
        public string Token { get; set; }
        public string LocalIp { get; set; }
        public int LocalPort { get; set; }
    }

    public class SessionInfoCommand : ApiCommandBase
    {
        public string Token { get; set; }
        public string LocalIp { get; set; }
        public int LocalPort { get; set; }
        public string CityName { get; set; }
        public string HostName { get; set; }
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }
        public bool HasPassword { get; set; }
        public string GameVersion { get; set; }
        public string ModVersion { get; set; }
    }

    public class ServerUnregisterCommand : ApiCommandBase
    {
        public string Token { get; set; }
    }
}
