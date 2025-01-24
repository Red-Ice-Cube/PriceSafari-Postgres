using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace PriceSafari.Models
{
    public class ProductClass
    {
        [Key]
        public int ProductId { get; set; }

        [ForeignKey("StoreClass")]
        public int StoreId { get; set; }
        public StoreClass Store { get; set; }

        // Podstawowe pola
        public string ProductName { get; set; }
        public string? Category { get; set; }

        // Kiedy produkt został dodany do systemu:
        public DateTime AddedDate { get; set; } = DateTime.UtcNow;
        // ^ Możesz użyć DateTime.Now, zależnie od tego czy chcesz UTC czy lokalny czas.

        // ----------------
        // Ceneo
        // ----------------
        private string? offerUrl;
        public string? OfferUrl
        {
            get => offerUrl;
            set
            {
                // Gdy przypiszemy nową, niepustą wartość do OfferUrl,
                // i FoundOnCeneoDate jest jeszcze puste, ustawimy datę.
                offerUrl = value;
                if (!string.IsNullOrEmpty(value) && FoundOnCeneoDate == null)
                {
                    FoundOnCeneoDate = DateTime.UtcNow;
                }
            }
        }

        // Data, kiedy faktycznie potwierdziliśmy obecność w Ceneo:
        public DateTime? FoundOnCeneoDate { get; set; }

        // ----------------
        // Google
        // ----------------
        private bool? foundOnGoogle;
        public bool? FoundOnGoogle
        {
            get => foundOnGoogle;
            set
            {
                foundOnGoogle = value;
                // Gdy ustawiamy `FoundOnGoogle` na true
                // i jeszcze nie mamy daty, ustaw ją:
                if (value == true && FoundOnGoogleDate == null)
                {
                    FoundOnGoogleDate = DateTime.UtcNow;
                }
            }
        }

        // Kiedy faktycznie potwierdzono obecność w Google
        public DateTime? FoundOnGoogleDate { get; set; }

        // GoogleShoping Block
        public bool OnGoogle { get; set; } = false;
        public string? Url { get; set; }
        public string? GoogleUrl { get; set; }
        public string? ProductNameInStoreForGoogle { get; set; }
        public string? EanGoogle { get; set; }
        public string? ImgUrlGoogle { get; set; }

        // Pozostałe pola
        public int? ExternalId { get; set; }
        public string? CatalogNumber { get; set; }
        public string? Ean { get; set; }
        public string? MainUrl { get; set; }
        public decimal? ExternalPrice { get; set; }
        public bool IsScrapable { get; set; } = false;
        public bool IsRejected { get; set; } = false;

        public string? ExportedNameCeneo { get; set; }
        public decimal? MarginPrice { get; set; }

        // Relacje
        public ICollection<PriceHistoryClass> PriceHistories { get; set; } = new List<PriceHistoryClass>();
        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();
    }
}
