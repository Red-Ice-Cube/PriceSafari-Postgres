using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Heat_Lead.Models.ViewModels
{
    public class NewsViewModel
    {
        public List<NewsIndexViewModel> NewsIndex { get; set; } = new List<NewsIndexViewModel>();

        public class NewsIndexViewModel
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
    }
}