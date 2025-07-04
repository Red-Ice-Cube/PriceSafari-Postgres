using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.ManagerViewModels
{
    public class CategoryManagementViewModel
    {
        public int? SelectedStoreId { get; set; }
        public string SelectedStoreName { get; set; }
        public List<StoreClass> AllStores { get; set; }
        public List<CategoryClass> CategoriesForSelectedStore { get; set; }

        public CategoryClass NewCategory { get; set; }

        public CategoryManagementViewModel()
        {
            NewCategory = new CategoryClass();
            AllStores = new List<StoreClass>();
            CategoriesForSelectedStore = new List<CategoryClass>();
        }
    }
    public class CreateCategoryDto
    {
        [Required]
        public int? StoreId { get; set; }

        [Required(ErrorMessage = "Nazwa kategorii jest wymagana.")]
        [StringLength(100)]
        public string CategoryName { get; set; }

        [Required(ErrorMessage = "URL kategorii jest wymagany.")]
        [StringLength(100)]
        public string CategoryUrl { get; set; }

        [Range(0, 10, ErrorMessage = "Głębokość musi być liczbą od 0 do 10.")]
        public int Depth { get; set; }
    }
}