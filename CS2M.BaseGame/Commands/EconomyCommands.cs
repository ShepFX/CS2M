using CS2M.API.Commands;

namespace CS2M.BaseGame.Commands
{
    /// <summary>
    ///     The full tax rate table (TaxSystem.m_TaxRates). Sent by whoever changed a tax slider.
    /// </summary>
    public class TaxCommand : CommandBase
    {
        public int[] Rates { get; set; }
    }

    /// <summary>
    ///     Service budgets: Services[i] (prefab name) is at Budgets[i] percent. Sent by whoever changed one.
    /// </summary>
    public class BudgetCommand : CommandBase
    {
        public string[] Services { get; set; }
        public int[] Budgets { get; set; }
    }

    /// <summary>
    ///     The host's population figures, shown on joiners' top bar.
    /// </summary>
    public class PopulationCommand : CommandBase
    {
        public int Population { get; set; }
        public int PopulationWithMoveIn { get; set; }
        public int AverageHappiness { get; set; }
        public int AverageHealth { get; set; }
    }
}
