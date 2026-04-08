using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace PriceSafari.Models.DTOs
{
    [XmlRoot("AllegroFeed")]
    public class AllegroExportFeedDto
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public string StoreNameAllegro { get; set; }
        public DateTime GeneratedAt { get; set; }

        [XmlArray("Products")]
        [XmlArrayItem("Product")]
        public List<AllegroExportProductDto> Products { get; set; } = new();
    }

    public class AllegroExportProductDto
    {
        public string AllegroProductName { get; set; }
        public string? AllegroEan { get; set; }
        public string? AllegroSku { get; set; }
        public string? Producer { get; set; }
        public string? AllegroOfferUrl { get; set; }
        public string? IdOnAllegro { get; set; }

        // Cena zakupu (do liczenia marży po stronie klienta)
        public decimal? PurchasePrice { get; set; }

        [XmlArray("Offers")]
        [XmlArrayItem("Offer")]
        public List<AllegroExportOfferDto> Offers { get; set; } = new();
    }

    public class AllegroExportOfferDto
    {
        public string SellerName { get; set; }
        public long IdAllegro { get; set; }
        public long? StoreIdOnAllegro { get; set; }
        public decimal Price { get; set; }
        public decimal? DeliveryCost { get; set; }
        public int? DeliveryTime { get; set; }
        public int? Popularity { get; set; }
        public bool SuperSeller { get; set; }
        public bool Smart { get; set; }
        public bool IsBestPriceGuarantee { get; set; }
        public bool TopOffer { get; set; }
        public bool SuperPrice { get; set; }
        public bool Promoted { get; set; }
        public bool Sponsored { get; set; }
        public int? RatingCount { get; set; }
        public double? RatingPositivePercent { get; set; }
    }
}