using PriceSafari.ScrapersControllers;
using System.Collections.Concurrent;

namespace PriceSafari.Models.ManagerViewModels
{
    public class AllegroGatherViewModel
    {
        public List<StoreClass> ScrapableStores { get; set; } = new();
        public List<AllegroProductClass> ScrapedProducts { get; set; } = new();

        // Zmieniamy int na słownik
        public ConcurrentDictionary<string, ScrapingStatus> ActiveTasks { get; set; }
    }
}
