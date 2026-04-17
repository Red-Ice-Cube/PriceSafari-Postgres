using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    // Tabela "robocza" łącząca Rekord Scrapingu (CoOfr) z konkretnym sklepem
    // Powstaje tylko dla sklepów z włączonym FetchExtendedData
    public class CoOfrStoreData
    {
        [Key]
        public int Id { get; set; }

        public int CoOfrClassId { get; set; }
        [ForeignKey("CoOfrClassId")]
        public virtual CoOfrClass CoOfr { get; set; }

        public int StoreId { get; set; }


        public string? ProductExternalId { get; set; }

        // Miejsce na wynik działania bota API
        [Column(TypeName = "decimal(18, 2)")]
        public decimal? ExtendedDataApiPrice { get; set; }

        /// <summary>
        /// Cena zakupu (minimalna) pobrana z API sklepu (np. productMinimalPrice z IdoSell)
        /// </summary>
        [Column(TypeName = "decimal(18, 2)")]
        public decimal? PurchasePriceFromApi { get; set; }

        // Flaga statusu - czy bot API już tu był i zapisał dane?
        public bool IsApiProcessed { get; set; } = false;
    }
}