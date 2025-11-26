using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public enum StoreSystemType
    {
        [Display(Name = "Inny / Niestandardowy")]
        Custom = 0,

        [Display(Name = "PrestaShop")]
        PrestaShop = 1,

        [Display(Name = "Shoper / ClickShop")]
        Shoper = 2,

        [Display(Name = "WooCommerce")]
        WooCommerce = 3,

        [Display(Name = "Magento")]
        Magento = 4,

        [Display(Name = "IdoSell (IAI)")]
        IdoSell = 5
    }
}