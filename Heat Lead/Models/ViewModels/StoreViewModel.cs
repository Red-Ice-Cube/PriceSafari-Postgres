namespace Heat_Lead.Models.ViewModels
{
    public class StoreViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }

        public string? Logo { get; set; }
    }

    public class CreateStoreViewModel
    {
        public string StoreName { get; set; }
        public string APIurl { get; set; }
        public string APIkey { get; set; }
        public string? LogoUrl { get; set; }
    }

    public class EditStoreViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public string APIurl { get; set; }
        public string APIkey { get; set; }
        public string? LogoUrl { get; set; }
    }
}