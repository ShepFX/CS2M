using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class DistrictNamesHandler : CommandHandler<DistrictNamesCommand>
    {
        public DistrictNamesHandler() { TransactionCmd = false; }
        protected override void Handle(DistrictNamesCommand command) => DistrictNameSyncSystem.Pending.Enqueue(command);
    }
}
