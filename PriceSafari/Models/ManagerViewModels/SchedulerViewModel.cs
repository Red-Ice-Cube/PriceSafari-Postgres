using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.ManagerViewModels
{
    public class SchedulerViewModel
    {
        // Dotychczasowe pola
        public ScheduledTask ScheduledTask { get; set; }
        public string ScheduledTime { get; set; }
        public bool IsEnabled { get; set; }


        public List<StoreClass> AutoMatchingStores { get; set; } = new List<StoreClass>();

        // Nowe pola dla akcji URL_SCAL
        public string UrlScheduledTime { get; set; }
        public bool UrlIsEnabled { get; set; }

    
    }


    public class ScheduledTaskInputModel
    {
        [Required(ErrorMessage = "Scheduled time is required.")]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid time format.")]
        public string ScheduledTime { get; set; }

        public bool IsEnabled { get; set; }



        [Required(ErrorMessage = "Scheduled time is required.")]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid time format.")]
        public string UrlScheduledTime { get; set; }

        public bool UrlIsEnabled { get; set; }


    }


}
