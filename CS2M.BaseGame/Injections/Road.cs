using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class RoadHandler : CommandHandler<RoadCommand>
    {
        public RoadHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(RoadCommand command)
        {
            RoadApplySystem.Pending.Enqueue(command);
        }
    }
}
