using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web; // Może wymagać System.Web w starszych .NET lub System.Net w nowszych

namespace PriceSafari.Models
{
    public class AllegroProductClass
    {
        [Key]
        public int AllegroProductId { get; set; }

        [ForeignKey("StoreClass")]
        public int StoreId { get; set; }
        public StoreClass Store { get; set; }

        public string AllegroProductName { get; set; }

        public string AllegroOfferUrl { get; set; }

        // --- NOWE POLE 
        [MaxLength(50)]
        public string? IdOnAllegro { get; set; }

        public DateTime AddedDate { get; set; } = DateTime.UtcNow;

        public decimal? AllegroMarginPrice { get; set; }

        public string? AllegroEan { get; set; }

        public bool IsScrapable { get; set; } = false;
        public bool IsRejected { get; set; } = false;

        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();

        // --- LOGIKA BIZNESOWA WBUDOWANA W KLASĘ ---

        /// <summary>
        /// Wywołaj tę metodę, aby przeliczyć i zapisać IdOnAllegro na podstawie obecnego URL-a.
        /// </summary>
        public void CalculateIdFromUrl()
        {
            if (string.IsNullOrWhiteSpace(this.AllegroOfferUrl))
            {
                this.IdOnAllegro = null;
                return;
            }

            this.IdOnAllegro = ExtractIdInternal(this.AllegroOfferUrl);
        }

        // Prywatna logika parsowania (niedostępna na zewnątrz, tylko dla tej klasy)
        private static string? ExtractIdInternal(string url)
        {
            try
            {
                // Przypadek 1: URL z parametrem ?offerId=12345
                if (url.Contains("offerId=", StringComparison.OrdinalIgnoreCase))
                {
                    // Proste wyciąganie bez zewnętrznych bibliotek, żeby nie dodawać zależności
                    var queryIndex = url.IndexOf("offerId=", StringComparison.OrdinalIgnoreCase);
                    var start = queryIndex + 8; // długość "offerId="
                    var end = url.IndexOf('&', start);

                    if (end == -1) return url.Substring(start);
                    return url.Substring(start, end - start);
                }

                // Przypadek 2: Standardowy format .../nazwa-123456789
                // Usuwamy ewentualny slash na końcu
                var cleanUrl = url.TrimEnd('/');
                var parts = cleanUrl.Split('-');
                var lastPart = parts.LastOrDefault();

                // Sprawdzamy czy ostatnia część to liczba (ID)
                if (long.TryParse(lastPart, out _))
                {
                    return lastPart;
                }

                // Przypadek 3 (Fallback): Regex szukający ciągu 10-12 cyfr
                var match = Regex.Match(url, @"(\d{10,})");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch
            {
                // W razie błędu parsowania
                return null;
            }

            return null;
        }
    }
}