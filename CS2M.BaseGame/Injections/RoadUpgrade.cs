using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class RoadUpgradeHandler : CommandHandler<RoadUpgradeCommand>
    {
        public RoadUpgradeHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(RoadUpgradeCommand command)
        {
            RoadUpgradeApplySystem.Pending.Enqueue(command);
        }
    }
}
