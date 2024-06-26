using System.ComponentModel.DataAnnotations;

namespace Heat_Lead.Models.ManagerViewModels
{
    public class ManagerCategoryViewModel
    {
        public List<ManagerCategoryDetailsViewModel> CategoryDetails { get; set; }

        public class ManagerCategoryDetailsViewModel
        {
            public int CategoryId { get; set; }

            [Required(ErrorMessage = "Nazwa jest wymagana")]
            [StringLength(50, ErrorMessage = "Maksymalna długość 24 znaki")]
            public string CategoryName { get; set; }

            public int Validation { get; set; }
            public bool CodeTracking { get; set; }
            public decimal? CommissionPercentage { get; set; }

            public int NumberOfProducts { get; set; }
        }

        public class ManagerCategoryEditViewModel
        {
            public int CategoryId { get; set; }

            [Required(ErrorMessage = "Nazwa jest wymagana")]
            [StringLength(50, ErrorMessage = "Maksymalna długość 24 znaki")]
            public string CategoryName { get; set; }

            public int Validation { get; set; }
            public bool CodeTracking { get; set; }

            public int? StoreId { get; set; }
            public decimal? CommissionPercentage { get; set; }
            public int NumberOfProducts { get; set; }
        }
    }
}
