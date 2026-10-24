using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    // Transport lines; all applied in order by TransitLineApplySystem on the game update.

    public class TransitLineHandler : CommandHandler<TransitLineCommand>
    {
        public TransitLineHandler() { TransactionCmd = false; }
        protected override void Handle(TransitLineCommand command) => TransitLineApplySystem.Pending.Enqueue(command);
    }

    public class TransitLineDeleteHandler : CommandHandler<TransitLineDeleteCommand>
    {
        public TransitLineDeleteHandler() { TransactionCmd = false; }
        protected override void Handle(TransitLineDeleteCommand command) => TransitLineApplySystem.Pending.Enqueue(command);
    }

    public class TransitLineSettingsHandler : CommandHandler<TransitLineSettingsCommand>
    {
        public TransitLineSettingsHandler() { TransactionCmd = false; }
        protected override void Handle(TransitLineSettingsCommand command) => TransitLineApplySystem.Pending.Enqueue(command);
    }
}
