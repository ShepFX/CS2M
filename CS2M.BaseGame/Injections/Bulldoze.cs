using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class BulldozeHandler : CommandHandler<BulldozeCommand>
    {
        public BulldozeHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(BulldozeCommand command)
        {
            BulldozeApplySystem.Pending.Enqueue(command);
        }
    }
}
