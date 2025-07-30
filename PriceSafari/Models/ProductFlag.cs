using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class ProductFlag
    {
        [Key]
        public int ProductFlagId { get; set; } // Nowy, prosty klucz główny

        // --- Klucz obcy do flagi (pozostaje bez zmian) ---
        [Required]
        public int FlagId { get; set; }
        [ForeignKey("FlagId")]
        public FlagsClass Flag { get; set; }

        // --- Klucz obcy do produktu Ceneo/Google (teraz opcjonalny) ---
        public int? ProductId { get; set; }
        [ForeignKey("ProductId")]
        public ProductClass Product { get; set; }

        // --- NOWY klucz obcy do produktu Allegro (też opcjonalny) ---
        public int? AllegroProductId { get; set; }
        [ForeignKey("AllegroProductId")]
        public AllegroProductClass AllegroProduct { get; set; }
    }
}