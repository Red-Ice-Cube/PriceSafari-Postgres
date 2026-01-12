using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

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

        [MaxLength(50)]
        public string? IdOnAllegro { get; set; }

        public DateTime AddedDate { get; set; } = DateTime.UtcNow;

        public decimal? AllegroMarginPrice { get; set; }

        public string? AllegroEan { get; set; }

        public bool IsScrapable { get; set; } = false;
        public bool IsRejected { get; set; } = false;

        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();

        // <summary>

        // Wywołaj tę metodę, aby przeliczyć i zapisać IdOnAllegro na podstawie obecnego URL-a.

        // </summary>

        public void CalculateIdFromUrl()
        {
            if (string.IsNullOrWhiteSpace(this.AllegroOfferUrl))
            {
                this.IdOnAllegro = null;
                return;
            }

            this.IdOnAllegro = ExtractIdInternal(this.AllegroOfferUrl);
        }

        private static string? ExtractIdInternal(string url)
        {
            try
            {

                if (url.Contains("offerId=", StringComparison.OrdinalIgnoreCase))
                {

                    var queryIndex = url.IndexOf("offerId=", StringComparison.OrdinalIgnoreCase);
                    var start = queryIndex + 8;

                    var end = url.IndexOf('&', start);

                    if (end == -1) return url.Substring(start);
                    return url.Substring(start, end - start);
                }

                var cleanUrl = url.TrimEnd('/');
                var parts = cleanUrl.Split('-');
                var lastPart = parts.LastOrDefault();

                if (long.TryParse(lastPart, out _))
                {
                    return lastPart;
                }

                var match = Regex.Match(url, @"(\d{10,})");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch
            {

                return null;
            }

            return null;
        }
    }
}