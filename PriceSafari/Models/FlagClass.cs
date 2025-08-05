// W pliku Models/FlagsClass.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema; // Upewnij się, że ten using jest dodany
using System.Collections.Generic;

namespace PriceSafari.Models
{
    public class FlagsClass
    {
        [Key]
        public int FlagId { get; set; }

        [Required]
        public string FlagName { get; set; }

        [Required]
        public string FlagColor { get; set; }

        [ForeignKey("Store")] // Ten atrybut wskazuje na poniższą właściwość nawigacyjną
        public int StoreId { get; set; }
        public StoreClass Store { get; set; } // Ta właściwość była brakująca

        public bool IsMarketplace { get; set; } = false;

        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();
    }
}