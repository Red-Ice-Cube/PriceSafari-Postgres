using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class PlanClass
    {
        [Key]
        public int PlanId { get; set; }
        public string PlanName { get; set; }
        public decimal NetPrice { get; set; } // Net price of the plan
        public int DurationDays { get; set; } // Duration of the plan in days
        public bool IsTestPlan { get; set; } = false; // Indicates if it's a test plan

        // Navigation property
        public ICollection<StoreClass> Stores { get; set; } = new List<StoreClass>();
    }
}
