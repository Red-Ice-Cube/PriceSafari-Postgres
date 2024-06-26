namespace Heat_Lead.Models.ViewModels
{
    public class CategoryViewModel
    {
        public int CategoryId { get; set; }
        public string CategoryImage { get; set; }
        public string CategoryName { get; set; }
        public string StoreName { get; set; }
        public List<ProductViewModel> Products { get; set; }
        public string GeneratedCodeAFI { get; set; }
        public int Validation { get; set; }
        public Category Category { get; set; }
        public string UserFullName { get; set; }
        public string Coupon { get; set; }
        public string CouponImage { get; set; }
    }

    public class ProductViewModel
    {
        public string ProductImage { get; set; }
        public string ProductName { get; set; }
        public decimal AffiliateCommission { get; set; }
        public string ProductURL { get; set; }
        public decimal ProductPrice { get; set; }

        public string AffiliateURL { get; set; }
    }
}