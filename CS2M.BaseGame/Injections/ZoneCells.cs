using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class ZoneCellsHandler : CommandHandler<ZoneCellsCommand>
    {
        public ZoneCellsHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(ZoneCellsCommand command)
        {
            // Entity changes happen in ZoneApplySystem on the game's update, not on the network callback
            ZoneApplySystem.Pending.Enqueue(command);
        }
    }
}
