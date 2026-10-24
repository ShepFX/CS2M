namespace CS2M.Commands.ApiServer
{
    /// <summary>
    ///     Removes the game server from the API server's list when the host stops it.
    /// </summary>
    /// Sent by:
    /// - Server
    public class ServerUnregisterCommand : ApiCommandBase
    {
        /// <summary>
        ///     The server token to remove.
        /// </summary>
        public string Token { get; set; }
    }
}
