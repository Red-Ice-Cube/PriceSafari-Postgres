// W folderze /Models
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    /// <summary>
    /// Przechowuje szczegółowy log pojedynczej zmiany ceny oferty Allegro
    /// w ramach jednej paczki (AllegroPriceBridgeBatch).
    /// </summary>
    public class AllegroPriceBridgeItem
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Powiązanie z nadrzędną paczką zmian.
        /// </summary>
        [Required]
        public int AllegroPriceBridgeBatchId { get; set; }
        [ForeignKey("AllegroPriceBridgeBatchId")]
        public virtual AllegroPriceBridgeBatch PriceBridgeBatch { get; set; }

        /// <summary>
        /// Powiązanie z produktem w systemie PriceSafari.
        /// </summary>
        [Required]
        public int AllegroProductId { get; set; }
        [ForeignKey("AllegroProductId")]
        public virtual AllegroProductClass AllegroProduct { get; set; }

        /// <summary>
        /// ID oferty Allegro, której cena została zmieniona.
        /// </summary>
        [Required]
        public string AllegroOfferId { get; set; }

        /// <summary>
        /// Status operacji (true = sukces, false = błąd).
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Ewentualny komunikat błędu, jeśli Success = false.
        /// </summary>
        public string ErrorMessage { get; set; }

        // --- STAN PRZED ZMIANĄ (z symulacji) ---

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CommissionBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginAmountBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginPercentBefore { get; set; }

        public string RankingBefore { get; set; }

        // --- STAN PO ZMIANIE (wgrywany) ---

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceAfter_Simulated { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CommissionAfter_Simulated { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginAmountAfter_Simulated { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginPercentAfter_Simulated { get; set; }

        public string RankingAfter_Simulated { get; set; }

        // --- STAN PO WERYFIKACJI API (rzeczywisty) ---

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PriceAfter_Verified { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CommissionAfter_Verified { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginAmountAfter_Verified { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginPercentAfter_Verified { get; set; }
    }
}