using System.ComponentModel.DataAnnotations;

namespace Heat_Lead.Models.ManagerViewModels
{
    public class ManagerProductViewModel
    {
        public List<ManagerProductDetailsViewModel> ProductDetails { get; set; }

        public class ManagerProductDetailsViewModel
        {
            public int ProductId { get; set; }

            [Required(ErrorMessage = "Nazwa jest wymagana")]
            [StringLength(50, ErrorMessage = "Maksymalna długość 50 znaków")]
            public string ProductName { get; set; }

            [Required(ErrorMessage = "ID produktu jest wymagane")]
            public List<string> ProductIdStores { get; set; }

            [Required(ErrorMessage = "Podanie ceny jest obowiązkowe")]
            [RegularExpression(@"^\d+(\,\d{1,2})?$", ErrorMessage = "Format ceny jest niepoprawny")]
            public decimal ProductPrice { get; set; }

            [Required(ErrorMessage = "Podanie prowizji jest obowiązkowe")]
            [RegularExpression(@"^\d+(\,\d{1,2})?$", ErrorMessage = "Format prowizji jest niepoprawny")]
            public decimal AffiliateCommission { get; set; }

            [Required(ErrorMessage = "URL do produktu jest wymagany")]
            public string ProductURL { get; set; }

            public string ProductImage { get; set; }

            public string? ProductCategory { get; set; }
        }

        public class ManagerProductEditViewModel
        {
            public int ProductId { get; set; }

            [Required(ErrorMessage = "Nazwa jest wymagana")]
            [StringLength(50, ErrorMessage = "Maksymalna długość 50 znaków")]
            public string ProductName { get; set; }

            [Required(ErrorMessage = "ID produktu jest wymagane")]
            public List<string> ProductIdStores { get; set; }

            [Required(ErrorMessage = "Podanie ceny jest obowiązkowe")]
            [RegularExpression(@"^\d+(\,\d{1,2})?$", ErrorMessage = "Format ceny jest niepoprawny")]
            public decimal ProductPrice { get; set; }

            [Required(ErrorMessage = "Podanie prowizji jest obowiązkowe")]
            [RegularExpression(@"^\d+(\,\d{1,2})?$", ErrorMessage = "Format prowizji jest niepoprawny")]
            public decimal AffiliateCommission { get; set; }

            [Required(ErrorMessage = "URL do produktu jest wymagany")]
            public string ProductURL { get; set; }

            public string ProductImage { get; set; }

            [Required(ErrorMessage = "Wybór kategorii jest wymagany")]
            public int? CategoryId { get; set; }
        }
    }
}