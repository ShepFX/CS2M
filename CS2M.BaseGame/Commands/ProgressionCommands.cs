using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     The host's city progression, sent every second: XP, milestone, development points, credit limit
    ///     and loan. Joiners overwrite theirs with it.
    /// </summary>
    public class ProgressionCommand : CommandBase
    {
        public int XP { get; set; }
        public int MaximumPopulation { get; set; }
        public int MaximumIncome { get; set; }
        public int XPRewardRecord { get; set; }
        public int Milestone { get; set; }
        public int DevTreePoints { get; set; }
        public int Creditworthiness { get; set; }
        public int LoanAmount { get; set; }
        public uint LoanLastModified { get; set; }
    }

    /// <summary>
    ///     The development tree nodes the host has unlocked (prefab names). Sent when it changes and every few seconds.
    /// </summary>
    public class DevTreeCommand : CommandBase
    {
        public string[] Nodes { get; set; }
    }

    /// <summary>
    ///     Joiner to host: this player set the loan to Amount. The host applies it through its LoanSystem.
    /// </summary>
    public class LoanRequestCommand : CommandBase
    {
        public int Amount { get; set; }
    }

    /// <summary>
    ///     Joiner to host: this player bought a development tree node.
    /// </summary>
    public class DevTreeRequestCommand : CommandBase
    {
        public string Node { get; set; }
    }

    /// <summary>
    ///     The map tiles the host owns, each as its centre (x, z pairs). Sent when it changes and every few seconds.
    /// </summary>
    public class MapTilesCommand : CommandBase
    {
        public float[] Owned { get; set; }
    }

    /// <summary>
    ///     Joiner to host: this player bought these tiles (centres as x, z pairs) for Cost.
    /// </summary>
    public class TilePurchaseRequestCommand : CommandBase
    {
        public float[] Tiles { get; set; }
        public int Cost { get; set; }
    }
}
