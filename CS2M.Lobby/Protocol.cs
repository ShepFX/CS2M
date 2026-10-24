using System.Buffers;
using MessagePack;

namespace CS2M.Lobby;

/// <summary>
///     The mod's API messages (CS2M/Commands/ApiServer). The mod serializes them with MessagePack.Attributeless,
///     which writes a bare sequence of MessagePack values, no surrounding array or map:
///     <code>
///     int typeKey          the subtype key, in the order ApiCommand.RefreshModel registers the commands
///     int propertyCount
///     (int propertyKey, value) * propertyCount
///                          property keys are the command's writable properties sorted by name, from 0
///     </code>
///     The whole thing is the payload of a LiteNetLib unconnected message.
/// </summary>
public static class Protocol
{
    // ApiCommand.ProtocolOrder in the mod. New commands only ever go at the end.
    public const int PortCheckRequest = 0;
    public const int PortCheckResult = 1;
    public const int ServerRegistration = 2;
    public const int SessionInfo = 3;
    public const int ServerUnregister = 4;

    public enum PortCheckState
    {
        Reachable = 0,
        Unreachable = 1,
        Error = 2
    }

    public abstract record Message;

    /// <summary>Properties: Port = 0</summary>
    public sealed record PortCheckRequestMessage(int Port) : Message;

    /// <summary>Properties: LocalIp = 0, LocalPort = 1, Token = 2</summary>
    public sealed record ServerRegistrationMessage(string? Token, string? LocalIp, int LocalPort) : Message;

    /// <summary>
    ///     Properties: CityName = 0, GameVersion = 1, HasPassword = 2, HostName = 3, LocalIp = 4, LocalPort = 5,
    ///     MaxPlayers = 6, ModVersion = 7, PlayerCount = 8, Token = 9
    /// </summary>
    public sealed record SessionInfoMessage(
        string? Token, string? LocalIp, int LocalPort,
        string? CityName, string? HostName, int PlayerCount, int MaxPlayers, bool HasPassword,
        string? GameVersion, string? ModVersion) : Message;

    /// <summary>Properties: Token = 0</summary>
    public sealed record ServerUnregisterMessage(string? Token) : Message;

    /// <summary>
    ///     Decodes a message from the mod. Unknown properties are skipped, so newer mods can add some;
    ///     unknown types return null.
    /// </summary>
    public static Message? Decode(ReadOnlyMemory<byte> data)
    {
        var reader = new MessagePackReader(data);
        if (reader.TryReadNil())
        {
            return null;
        }

        int type = reader.ReadInt32();
        if (type is < PortCheckRequest or > ServerUnregister)
        {
            return null;
        }

        int count = reader.ReadInt32();
        if (count is < 0 or > 64)
        {
            throw new MessagePackSerializationException($"Bad property count {count}");
        }

        int port = 0, localPort = 0, playerCount = 0, maxPlayers = 0;
        bool hasPassword = false;
        string? token = null, localIp = null, cityName = null, hostName = null, gameVersion = null, modVersion = null;

        for (int i = 0; i < count; i++)
        {
            int key = reader.ReadInt32();
            switch (type, key)
            {
                case (PortCheckRequest, 0): port = reader.ReadInt32(); break;

                case (ServerRegistration, 0): localIp = ReadString(ref reader); break;
                case (ServerRegistration, 1): localPort = reader.ReadInt32(); break;
                case (ServerRegistration, 2): token = ReadString(ref reader); break;

                case (SessionInfo, 0): cityName = ReadString(ref reader); break;
                case (SessionInfo, 1): gameVersion = ReadString(ref reader); break;
                case (SessionInfo, 2): hasPassword = reader.ReadBoolean(); break;
                case (SessionInfo, 3): hostName = ReadString(ref reader); break;
                case (SessionInfo, 4): localIp = ReadString(ref reader); break;
                case (SessionInfo, 5): localPort = reader.ReadInt32(); break;
                case (SessionInfo, 6): maxPlayers = reader.ReadInt32(); break;
                case (SessionInfo, 7): modVersion = ReadString(ref reader); break;
                case (SessionInfo, 8): playerCount = reader.ReadInt32(); break;
                case (SessionInfo, 9): token = ReadString(ref reader); break;

                case (ServerUnregister, 0): token = ReadString(ref reader); break;

                default: reader.Skip(); break;
            }
        }

        return type switch
        {
            PortCheckRequest => new PortCheckRequestMessage(port),
            ServerRegistration => new ServerRegistrationMessage(token, localIp, localPort),
            SessionInfo => new SessionInfoMessage(token, localIp, localPort, cityName, hostName, playerCount,
                maxPlayers, hasPassword, gameVersion, modVersion),
            ServerUnregister => new ServerUnregisterMessage(token),
            _ => null // PortCheckResult only goes from us to the mod
        };
    }

    /// <summary>PortCheckResultCommand, properties: Message = 0, State = 1 (enum as its int value).</summary>
    public static byte[] EncodePortCheckResult(PortCheckState state, string? message)
    {
        var buffer = new ArrayBufferWriter<byte>(64);
        var writer = new MessagePackWriter(buffer);
        writer.Write(PortCheckResult);
        writer.Write(2);
        writer.Write(0);
        writer.Write(message);
        writer.Write(1);
        writer.Write((int)state);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static string? ReadString(ref MessagePackReader reader)
    {
        return reader.TryReadNil() ? null : reader.ReadString();
    }
}
