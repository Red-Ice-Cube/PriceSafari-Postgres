using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class EmailTemplate
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Nazwa Szablonu")]
        public string Name { get; set; } // Np. "Powitanie Ceneo" - to co widzisz w dropdownie

        [Required]
        [Display(Name = "Temat Emaila")]
        public string Subject { get; set; } // Domyślny temat, np. "Współpraca z PriceSafari"

        [Display(Name = "Treść HTML")]
        public string Content { get; set; } // Tutaj wyląduje HTML z TinyMCE
    }
}