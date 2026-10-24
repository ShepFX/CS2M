using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class TaxHandler : CommandHandler<TaxCommand>
    {
        public TaxHandler() { TransactionCmd = false; }
        protected override void Handle(TaxCommand command) => EconomySyncSystem.Pending.Enqueue(command);
    }

    public class BudgetHandler : CommandHandler<BudgetCommand>
    {
        public BudgetHandler() { TransactionCmd = false; }
        protected override void Handle(BudgetCommand command) => EconomySyncSystem.Pending.Enqueue(command);
    }

    public class PopulationHandler : CommandHandler<PopulationCommand>
    {
        public PopulationHandler() { TransactionCmd = false; }
        protected override void Handle(PopulationCommand command) => PopulationDisplaySystem.Latest = command;
    }
}
