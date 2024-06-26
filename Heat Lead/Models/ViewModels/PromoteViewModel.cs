namespace Heat_Lead.Models.ViewModels
{
    using Heat_Lead.Models;
    using System.Collections.Generic;

    public class PromoteViewModel
    {
        public List<StoreWithCategories> StoresWithCategories { get; set; }
        public List<Product> Products { get; set; }
        public List<int> SelectedCategories { get; set; }
        public List<int> CartProductIds { get; set; } = new List<int>();
        public int CookieLifeTime { get; set; }
    }

    public class StoreWithCategories
    {
        public Store Store { get; set; }
        public IEnumerable<Category> Categories { get; set; }
    }

    public class AddToCartViewModel
    {
        public int? ProductId { get; set; }
    }
}