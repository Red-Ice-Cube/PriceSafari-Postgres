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

     
        public string ProductName { get; set; }
        public string? Category { get; set; }

       
        public DateTime AddedDate { get; set; } = DateTime.UtcNow;
    
        private string? offerUrl;
        public string? OfferUrl
        {
            get => offerUrl;
            set
            {
         
                offerUrl = value;
                if (!string.IsNullOrEmpty(value) && FoundOnCeneoDate == null)
                {
                    FoundOnCeneoDate = DateTime.UtcNow;
                }
            }
        }

 
        public DateTime? FoundOnCeneoDate { get; set; }


        private bool? foundOnGoogle;
        public bool? FoundOnGoogle
        {
            get => foundOnGoogle;
            set
            {
                foundOnGoogle = value;
        
                if (value == true && FoundOnGoogleDate == null)
                {
                    FoundOnGoogleDate = DateTime.UtcNow;
                }
            }
        }


        public DateTime? FoundOnGoogleDate { get; set; }


        public bool OnGoogle { get; set; } = false;
        public string? Url { get; set; }
        public string? GoogleUrl { get; set; }
        public string? ProductNameInStoreForGoogle { get; set; }
        public string? EanGoogle { get; set; }
        public string? ImgUrlGoogle { get; set; }

 
        public int? ExternalId { get; set; }
        public string? CatalogNumber { get; set; }
        public string? Ean { get; set; }
        public string? MainUrl { get; set; }
        public decimal? ExternalPrice { get; set; }
        public bool IsScrapable { get; set; } = false;
        public bool IsRejected { get; set; } = false;

        public string? ExportedNameCeneo { get; set; }
        public decimal? MarginPrice { get; set; }



        [Column(TypeName = "decimal(18, 2)")]
        public decimal? GoogleXMLPrice { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? GoogleDeliveryXMLPrice { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? CeneoXMLPrice { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? CeneoDeliveryXMLPrice { get; set; }


        // Relacje
        public ICollection<PriceHistoryClass> PriceHistories { get; set; } = new List<PriceHistoryClass>();
        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();
    }
}
