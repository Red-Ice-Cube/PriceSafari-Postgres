using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class ProductClass
    {
        [Key]
        public int ProductId { get; set; }
        [ForeignKey("StoreClass")]
        public int StoreId { get; set; }
        public StoreClass Store { get; set; }
        public string ProductName { get; set; } // nazwa produktu pobrana z ceneo
        public string? Category { get; set; } // kategoria pobrana z ceneo
        public string? OfferUrl { get; set; } // url oferty na ceneo 
        public int? ExternalId { get; set; }   // zewnetrzna ID do API
        public string? CatalogNumber { get; set; }
        public string? Ean { get; set; } // ean z ceneo 
        public string? MainUrl { get; set; } //zdjecie z ceneo url
        public decimal? ExternalPrice { get; set; }
        public bool IsScrapable { get; set; } = false;
        public bool IsRejected { get; set; } = false;

        //CeneoXML
        public string? ExportedNameCeneo { get; set; } //exportowana nazwa do ceneo

        //GoogleShoping Block

        public bool OnGoogle { get; set; } = false;
        public string? Url { get; set; }
        public string? GoogleUrl { get; set; }
        public string? ProductNameInStoreForGoogle { get; set; } //exportowana nazwa do google

        public string? EanGoogle { get; set; } // ean z google
        public string? ImgUrlGoogle { get; set; } //zdjecie z google url
        public bool? FoundOnGoogle { get; set; }



        //Marza
        public decimal? MarginPrice { get; set; }


        public ICollection<PriceHistoryClass> PriceHistories { get; set; } = new List<PriceHistoryClass>();
        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();

    }
}
