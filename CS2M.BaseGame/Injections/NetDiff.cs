using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class NetDiffHandler : CommandHandler<NetDiffCommand>
    {
        public NetDiffHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(NetDiffCommand command)
        {
            NetDiffApplySystem.Pending.Enqueue(command);
        }
    }
}
