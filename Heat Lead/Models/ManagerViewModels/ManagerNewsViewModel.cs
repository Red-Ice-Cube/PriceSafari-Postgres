using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Heat_Lead.Models.ManagerViewModels
{
    public class ManagerNewsViewModel
    {
        public List<ManagerNewsIndexViewModel> NewsIndex { get; set; }

        public class ManagerNewsIndexViewModel
        {
            public int NewsId { get; set; }

            [Required]
            [Display(Name = "Tytuł")]
            public string? Title { get; set; }

            [Required]
            [Display(Name = "Treść Wiadomości")]
            public string? Message { get; set; }

            [Display(Name = "URL Grafiki")]
            public string? GraphicUrl { get; set; }

            [Display(Name = "Data Utworzenia")]
            public DateTime CreationDate { get; set; }

            public string FormattedCreationDate
            {
                get
                {
                    return CreationDate.ToString("d", new CultureInfo("pl-PL"));
                }
            }
        }

        public class ManagerNewsCreateViewModel
        {
            [Required]
            [Display(Name = "Tytuł")]
            public string? Title { get; set; }

            [Required]
            [Display(Name = "Treść Wiadomości")]
            public string? Message { get; set; }

            [Display(Name = "URL Grafiki")]
            public string? GraphicUrl { get; set; }
        }

        public class ManagerNewsEditViewModel
        {
            public int NewsId { get; set; }

            [Required]
            [Display(Name = "Tytuł")]
            public string? Title { get; set; }

            [Required]
            [Display(Name = "Treść Wiadomości")]
            public string? Message { get; set; }

            [Display(Name = "URL Grafiki")]
            public string? GraphicUrl { get; set; }

            [Display(Name = "Data Utworzenia")]
            public DateTime CreationDate { get; set; }
        }
    }
}