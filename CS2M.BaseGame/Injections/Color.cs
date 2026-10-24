using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class ColorHandler : CommandHandler<ColorCommand>
    {
        public ColorHandler() { TransactionCmd = false; }
        protected override void Handle(ColorCommand command) => ColorSyncSystem.Pending.Enqueue(command);
    }
}
