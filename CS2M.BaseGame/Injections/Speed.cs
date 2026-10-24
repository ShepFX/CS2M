using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class SpeedHandler : CommandHandler<SpeedCommand>
    {
        public SpeedHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(SpeedCommand command)
        {
            SpeedSyncSystem.Pending.Enqueue(command);
        }
    }
}
