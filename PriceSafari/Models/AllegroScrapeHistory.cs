using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    /// <summary>
    /// Zapisuje podsumowanie pojedynczej sesji przetwarzania danych z Allegro dla konkretnego sklepu.
    /// </summary>
    public class AllegroScrapeHistory
    {
        [Key]
        public int Id { get; set; }

        public DateTime Date { get; set; } = DateTime.UtcNow;

        // Klucz obcy do sklepu, którego dotyczyło przetwarzanie
        public int StoreId { get; set; }

        [ForeignKey("StoreId")]
        public virtual StoreClass Store { get; set; }

        /// <summary>
        /// Liczba unikalnych URL-i (z AllegroOfferToScrape), które przetworzono w tej sesji.
        /// </summary>
        public int ProcessedUrlsCount { get; set; }

        /// <summary>
        /// Łączna liczba "czystych" ofert cenowych zapisanych w tej sesji do AllegroPriceHistory.
        /// </summary>
        public int SavedOffersCount { get; set; }

        // Właściwość nawigacyjna do wszystkich cen zapisanych w tej sesji
        public virtual ICollection<AllegroPriceHistory> PriceHistories { get; set; } = new List<AllegroPriceHistory>();
    }
}