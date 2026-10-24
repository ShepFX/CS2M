using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    // Host to joiners: the host's progression. Joiners to host: requests the host applies once.

    public class ProgressionHandler : CommandHandler<ProgressionCommand>
    {
        public ProgressionHandler() { TransactionCmd = false; }
        protected override void Handle(ProgressionCommand command) => ProgressionSyncSystem.Pending.Enqueue(command);
    }

    public class DevTreeHandler : CommandHandler<DevTreeCommand>
    {
        public DevTreeHandler() { TransactionCmd = false; }
        protected override void Handle(DevTreeCommand command) => ProgressionSyncSystem.Pending.Enqueue(command);
    }

    public class LoanRequestHandler : CommandHandler<LoanRequestCommand>
    {
        public LoanRequestHandler()
        {
            TransactionCmd = false;
            // only the host applies it; the result reaches everyone through ProgressionCommand and MoneyCommand
            RelayOnServer = false;
        }

        protected override void Handle(LoanRequestCommand command) => ProgressionSyncSystem.Pending.Enqueue(command);
    }

    public class DevTreeRequestHandler : CommandHandler<DevTreeRequestCommand>
    {
        public DevTreeRequestHandler()
        {
            TransactionCmd = false;
            RelayOnServer = false;
        }

        protected override void Handle(DevTreeRequestCommand command) => ProgressionSyncSystem.Pending.Enqueue(command);
    }

    public class MapTilesHandler : CommandHandler<MapTilesCommand>
    {
        public MapTilesHandler() { TransactionCmd = false; }
        protected override void Handle(MapTilesCommand command) => MapTileSyncSystem.Pending.Enqueue(command);
    }

    public class TilePurchaseRequestHandler : CommandHandler<TilePurchaseRequestCommand>
    {
        public TilePurchaseRequestHandler()
        {
            TransactionCmd = false;
            RelayOnServer = false;
        }

        protected override void Handle(TilePurchaseRequestCommand command) => MapTileSyncSystem.Pending.Enqueue(command);
    }
}
