using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class CursorHandler : CommandHandler<CursorCommand>
    {
        public CursorHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(CursorCommand command)
        {
            CursorSystem.OnRemoteCursor(command);
        }
    }
}
