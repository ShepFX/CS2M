using System.Collections.Concurrent;
using CS2M.BaseGame.Commands;
using Game;
using Game.Common;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Applies zoning received from other players to the matching local block,
    ///     with the same rules as the game's ApplyZonesSystem.
    /// </summary>
    public partial class ZoneApplySystem : GameSystemBase
    {
        public static readonly ConcurrentQueue<ZoneCellsCommand> Pending = new ConcurrentQueue<ZoneCellsCommand>();

        private EntityQuery _blocks;

        protected override void OnCreate()
        {
            base.OnCreate();
            _blocks = GetEntityQuery(
                ComponentType.ReadOnly<Block>(),
                ComponentType.ReadOnly<Cell>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Deleted>());
        }

        protected override void OnUpdate()
        {
            // received while joining: wait until the city has loaded
            if (Pending.IsEmpty || CS2M.API.Commands.Command.CurrentRole == CS2M.API.Commands.MultiplayerRole.None)
            {
                return;
            }

            using (NativeArray<Entity> entities = _blocks.ToEntityArray(Allocator.Temp))
            using (NativeArray<Block> blocks = _blocks.ToComponentDataArray<Block>(Allocator.Temp))
            {
                while (Pending.TryDequeue(out ZoneCellsCommand command))
                {
                    Entity target = FindBlock(entities, blocks, command);
                    if (target == Entity.Null)
                    {
                        BaseGameLog.Warn($"Zoning received for a block that doesn't exist here ({command.PosX}, {command.PosY}, {command.PosZ})");
                        continue;
                    }

                    DynamicBuffer<Cell> cells = EntityManager.GetBuffer<Cell>(target);
                    for (int i = 0; i < command.Cells.Length; i++)
                    {
                        int index = command.Cells[i];
                        if (index < 0 || index >= cells.Length)
                        {
                            continue;
                        }

                        Cell cell = cells[index];
                        var zone = new ZoneType { m_Index = command.Zones[i] };
                        if ((cell.m_State & CellFlags.Overridden) != 0)
                        {
                            if (cell.m_Zone.Equals(zone))
                            {
                                cell.m_State &= ~CellFlags.Overridden;
                            }
                        }
                        else
                        {
                            cell.m_Zone = zone;
                        }

                        cells[index] = cell;
                    }

                    if (!EntityManager.HasComponent<Updated>(target))
                    {
                        EntityManager.AddComponent<Updated>(target);
                    }

                    BaseGameLog.Info($"Zoning received: {command.Cells.Length} cells on block at ({command.PosX}, {command.PosY}, {command.PosZ})");
                }
            }
        }

        private static Entity FindBlock(NativeArray<Entity> entities, NativeArray<Block> blocks, ZoneCellsCommand command)
        {
            var position = new float3(command.PosX, command.PosY, command.PosZ);
            var direction = new float2(command.DirX, command.DirY);
            for (int i = 0; i < blocks.Length; i++)
            {
                Block block = blocks[i];
                if (block.m_Size.x == command.SizeX && block.m_Size.y == command.SizeY &&
                    math.distancesq(block.m_Position, position) < 0.01f &&
                    math.dot(block.m_Direction, direction) > 0.999f)
                {
                    return entities[i];
                }
            }

            return Entity.Null;
        }
    }
}
