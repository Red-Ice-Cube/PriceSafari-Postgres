using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.ManagerViewModels
{
    public class SchedulerViewModel
    {
        public ScheduledTask ScheduledTask { get; set; }
        public string ScheduledTime { get; set; }
        public bool IsEnabled { get; set; }

        public string UrlScheduledTime { get; set; }
        public bool UrlIsEnabled { get; set; }

        // Google
        public string GoogleScheduledTime { get; set; }
        public bool GoogleIsEnabled { get; set; }


        // Ceneo
        public string CeneoScheduledTime { get; set; }
        public bool CeneoIsEnabled { get; set; }

        public List<StoreClass> AutoMatchingStores { get; set; } = new List<StoreClass>();
    }



    public class ScheduledTaskInputModel
    {
        // Base scraping
        [Required]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid time format.")]
        public string ScheduledTime { get; set; }
        public bool IsEnabled { get; set; }

        // URL scraping
        [Required]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid time format.")]
        public string UrlScheduledTime { get; set; }
        public bool UrlIsEnabled { get; set; }

        // Google scraping
        [Required]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid time format.")]
        public string GoogleScheduledTime { get; set; }
        public bool GoogleIsEnabled { get; set; }


        // Ceneo scraping
        [Required]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid time format.")]
        public string CeneoScheduledTime { get; set; }
        public bool CeneoIsEnabled { get; set; }

    }



}
