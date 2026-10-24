using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class PlaceHandler : CommandHandler<PlaceCommand>
    {
        public PlaceHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(PlaceCommand command)
        {
            PlaceApplySystem.Pending.Enqueue(command);
        }
    }
}
