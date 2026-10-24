using System.Collections.Generic;
using System.Reflection;
using CS2M.API.Commands;
using CS2M.BaseGame.Commands;
using Game;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace CS2M.BaseGame.Systems
{
    /// <summary>
    ///     Budget panel figures come from each game's own simulation, and joiners don't run building upkeep,
    ///     so the host sends its figures every 2 s and joiners write them in just before the UI reads them.
    ///     Per-service upkeep is answered from <see cref="TryGetHostUpkeep"/> by a patch in the main mod.
    /// </summary>
    public partial class BudgetFiguresSystem : GameSystemBase
    {
        private const float SendInterval = 2f;

        private static readonly FieldInfo IncomeField =
            typeof(CityServiceBudgetSystem).GetField("m_Income", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ExpensesField =
            typeof(CityServiceBudgetSystem).GetField("m_Expenses", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DepsField =
            typeof(CityServiceBudgetSystem).GetField("m_TempArrayDeps", BindingFlags.Instance | BindingFlags.NonPublic);

        public static BudgetFiguresCommand Latest;
        private static readonly Dictionary<string, int> HostUpkeep = new Dictionary<string, int>();
        private static PrefabSystem _prefabs;

        private CityServiceBudgetSystem _budgetSystem;
        private EntityQuery _budgetQuery;
        private float _lastSend;

        protected override void OnCreate()
        {
            base.OnCreate();
            _budgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _budgetQuery = GetEntityQuery(ComponentType.ReadOnly<ServiceBudgetData>());
        }

        /// <summary>
        ///     Joiners: the host's estimated upkeep for this service, if known.
        /// </summary>
        public static bool TryGetHostUpkeep(Entity service, out int upkeep)
        {
            upkeep = 0;
            if (Command.CurrentRole != MultiplayerRole.Client || _prefabs == null)
            {
                return false;
            }

            string name = PrefabLookup.Name(_prefabs, service);
            return name != null && HostUpkeep.TryGetValue(name, out upkeep);
        }

        public static void Receive(BudgetFiguresCommand command)
        {
            Latest = command;
            HostUpkeep.Clear();
            for (int i = 0; i < (command.Services?.Length ?? 0) && i < (command.Upkeep?.Length ?? 0); i++)
            {
                HostUpkeep[command.Services[i]] = command.Upkeep[i];
            }
        }

        protected override void OnUpdate()
        {
            MultiplayerRole role = Command.CurrentRole;
            if (role == MultiplayerRole.None || IncomeField == null || ExpensesField == null)
            {
                Latest = null;
                HostUpkeep.Clear();
                return;
            }

            if (DepsField?.GetValue(_budgetSystem) is JobHandle deps)
            {
                deps.Complete();
            }

            var income = (NativeArray<int>)IncomeField.GetValue(_budgetSystem);
            var expenses = (NativeArray<int>)ExpensesField.GetValue(_budgetSystem);
            if (!income.IsCreated || !expenses.IsCreated)
            {
                return;
            }

            if (role == MultiplayerRole.Client)
            {
                if (Latest != null)
                {
                    Copy(Latest.Income, income);
                    Copy(Latest.Expenses, expenses);
                }

                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastSend < SendInterval)
            {
                return;
            }

            _lastSend = now;
            var services = new List<string>();
            var upkeep = new List<int>();
            if (!_budgetQuery.IsEmptyIgnoreFilter)
            {
                using (NativeArray<Entity> entities = _budgetQuery.ToEntityArray(Allocator.Temp))
                {
                    DynamicBuffer<ServiceBudgetData> buffer = EntityManager.GetBuffer<ServiceBudgetData>(entities[0], true);
                    var serviceEntities = new List<Entity>();
                    foreach (ServiceBudgetData data in buffer)
                    {
                        serviceEntities.Add(data.m_Service);
                    }

                    foreach (Entity service in serviceEntities)
                    {
                        string name = PrefabLookup.Name(_prefabs, service);
                        if (name == null)
                        {
                            continue;
                        }

                        _budgetSystem.GetEstimatedServiceBudget(service, out int value);
                        services.Add(name);
                        upkeep.Add(value);
                    }
                }
            }

            Command.SendToAll?.Invoke(new BudgetFiguresCommand
            {
                Income = income.ToArray(),
                Expenses = expenses.ToArray(),
                Services = services.ToArray(),
                Upkeep = upkeep.ToArray()
            });
        }

        private static void Copy(int[] from, NativeArray<int> to)
        {
            if (from == null)
            {
                return;
            }

            for (int i = 0; i < from.Length && i < to.Length; i++)
            {
                to[i] = from[i];
            }
        }
    }
}
