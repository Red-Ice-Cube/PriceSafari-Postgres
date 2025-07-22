using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace PriceSafari.Models
{
    public class AllegroOfferToScrape
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string AllegroOfferUrl { get; set; }

        public List<int> AllegroProductIds { get; set; } = new List<int>();

        public bool IsScraped { get; set; } = false;

        public bool IsRejected { get; set; } = false;

        public DateTime AddedDate { get; set; } = DateTime.UtcNow;

        public int CollectedPricesCount { get; set; } = 0;

        /// <summary>
        /// NOWA FLAGA: Oznacza, że zadanie zostało pobrane przez scrapera i jest w trakcie przetwarzania.
        /// </summary>
        public bool IsProcessing { get; set; } = false;

        /// <summary>
        /// NOWA WŁAŚCIWOŚĆ NAWIGACYJNA: Kolekcja zescrapowanych ofert dla tego URL-a.
        /// </summary>
        public virtual ICollection<AllegroScrapedOffer> ScrapedOffers { get; set; } = new List<AllegroScrapedOffer>();
    }
}