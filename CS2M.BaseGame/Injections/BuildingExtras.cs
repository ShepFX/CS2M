using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class BuildingPolicyHandler : CommandHandler<BuildingPolicyCommand>
    {
        public BuildingPolicyHandler() { TransactionCmd = false; }
        protected override void Handle(BuildingPolicyCommand command) => BuildingSettingsSyncSystem.Pending.Enqueue(command);
    }

    public class RenameHandler : CommandHandler<RenameCommand>
    {
        public RenameHandler() { TransactionCmd = false; }
        protected override void Handle(RenameCommand command) => NameSyncSystem.Pending.Enqueue(command);
    }
}
