using CS2M.BaseGame.Systems;
using Game.Simulation;
using HarmonyLib;
using Unity.Entities;

namespace CS2M.Patches
{
    /// <summary>
    ///     Joiners don't run building upkeep, so their per-service upkeep estimate would be wrong; show the host's.
    /// </summary>
    [HarmonyPatch(typeof(CityServiceBudgetSystem), nameof(CityServiceBudgetSystem.GetEstimatedServiceBudget),
        new[] { typeof(Entity), typeof(int) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
    public static class ServiceUpkeepPatch
    {
        public static void Postfix(Entity servicePrefab, ref int upkeep)
        {
            if (BudgetFiguresSystem.TryGetHostUpkeep(servicePrefab, out int host))
            {
                upkeep = host;
            }
        }
    }
}
