using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class AreaChangeHandler : CommandHandler<AreaChangeCommand>
    {
        public AreaChangeHandler() { TransactionCmd = false; }
        protected override void Handle(AreaChangeCommand command) => AreaApplySystem.Pending.Enqueue(command);
    }

    public class PolicyHandler : CommandHandler<PolicyCommand>
    {
        public PolicyHandler() { TransactionCmd = false; }
        protected override void Handle(PolicyCommand command) => PolicySyncSystem.Pending.Enqueue(command);
    }
}
