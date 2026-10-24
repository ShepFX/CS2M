using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using CS2M.BaseGame.Systems;

namespace CS2M.BaseGame.Injections
{
    public class TerrainBrushHandler : CommandHandler<TerrainBrushCommand>
    {
        public TerrainBrushHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(TerrainBrushCommand command)
        {
            // The heightmap is changed in TerrainBrushApplySystem on the game's update, not on the network callback
            TerrainBrushApplySystem.Pending.Enqueue(command);
        }
    }

    public class WaterSourceHandler : CommandHandler<WaterSourceCommand>
    {
        public WaterSourceHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(WaterSourceCommand command)
        {
            WaterSourceApplySystem.Pending.Enqueue(command);
        }
    }
}
