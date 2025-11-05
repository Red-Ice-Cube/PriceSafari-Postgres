// W folderze /Models
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    /// <summary>
    /// Reprezentuje pojedynczą operację (paczkę) wgrania zmian cen na Allegro.
    /// Jest powiązana ze sklepem i konkretną analizą (scrapem).
    /// </summary>
    public class AllegroPriceBridgeBatch
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Data i czas wykonania operacji wgrania zmian.
        /// </summary>
        [Required]
        public DateTime ExecutionDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// ID sklepu, dla którego wgrywano zmiany.
        /// </summary>
        [Required]
        public int StoreId { get; set; }
        [ForeignKey("StoreId")]
        public virtual StoreClass Store { get; set; }

        /// <summary>
        /// ID analizy (scrapu), na podstawie której wygenerowano te zmiany.
        /// </summary>
        [Required]
        public int AllegroScrapeHistoryId { get; set; }
        [ForeignKey("AllegroScrapeHistoryId")]
        public virtual AllegroScrapeHistory AllegroScrapeHistory { get; set; }

        /// <summary>
        /// ID użytkownika, który zainicjował zmianę.
        /// </summary>
        public string UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual PriceSafariUser User { get; set; }

        /// <summary>
        /// Liczba pomyślnie wgranych zmian w tej paczce.
        /// </summary>
        public int SuccessfulCount { get; set; } = 0;

        /// <summary>
        /// Liczba zmian zakończonych błędem w tej paczce.
        /// </summary>
        public int FailedCount { get; set; } = 0;

        /// <summary>
        /// Szczegółowe logi dla każdej zmienionej oferty w tej paczce.
        /// </summary>
        public virtual ICollection<AllegroPriceBridgeItem> BridgeItems { get; set; } = new List<AllegroPriceBridgeItem>();
    }
}