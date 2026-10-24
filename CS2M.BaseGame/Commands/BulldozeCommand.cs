using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     Everything one bulldozer click removed: junctions, roads and objects. Sent by: BulldozeCaptureSystem.
    /// </summary>
    public class BulldozeCommand : CommandBase
    {
        public NetRef[] Targets { get; set; }
    }
}
