using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    // The host's decisions about buildings and money; all handled by HostAuthoritySystem on the game update.

    public class SpawnHandler : CommandHandler<SpawnCommand>
    {
        public SpawnHandler() { TransactionCmd = false; }
        protected override void Handle(SpawnCommand command) => HostAuthoritySystem.Pending.Enqueue(command);
    }

    public class LevelUpHandler : CommandHandler<LevelUpCommand>
    {
        public LevelUpHandler() { TransactionCmd = false; }
        protected override void Handle(LevelUpCommand command) => HostAuthoritySystem.Pending.Enqueue(command);
    }

    public class AbandonHandler : CommandHandler<AbandonCommand>
    {
        public AbandonHandler() { TransactionCmd = false; }
        protected override void Handle(AbandonCommand command) => HostAuthoritySystem.Pending.Enqueue(command);
    }

    public class MoneyHandler : CommandHandler<MoneyCommand>
    {
        public MoneyHandler() { TransactionCmd = false; }
        protected override void Handle(MoneyCommand command) => HostAuthoritySystem.Pending.Enqueue(command);
    }

    public class ChargeHandler : CommandHandler<ChargeCommand>
    {
        public ChargeHandler()
        {
            TransactionCmd = false;
            // only the host charges; the new balance reaches everyone through MoneyCommand
            RelayOnServer = false;
        }

        protected override void Handle(ChargeCommand command) => HostAuthoritySystem.Pending.Enqueue(command);
    }
}
