public class ProductCoOfrViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string Category { get; set; }
    public string OfferUrl { get; set; }
    public string? GoogleOfferUrl { get; set; }
    public int? CoOfrId { get; set; }


    public string? ApiExternalId { get; set; }
    public decimal? ApiPrice { get; set; }
    public bool ApiProcessed { get; set; }
    public bool HasApiDataEntry { get; set; }
}

public class StoreProductsViewModel
{
    public string StoreName { get; set; }
    public List<ProductCoOfrViewModel> Products { get; set; }
}
