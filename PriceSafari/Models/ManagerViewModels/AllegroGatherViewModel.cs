// Plik: Models/ManagerViewModels/AllegroGatherViewModel.cs
using PriceSafari.ScrapersControllers;
using System.Collections.Concurrent;

namespace PriceSafari.Models.ManagerViewModels
{
    public class AllegroGatherViewModel
    {
        public List<StoreClass> ScrapableStores { get; set; } = new();
        public List<AllegroProductClass> ScrapedProducts { get; set; } = new();
        public ConcurrentDictionary<string, ScrapingTaskState> ActiveTasks { get; set; }

        // NOWA WŁAŚCIWOŚĆ
        public ConcurrentDictionary<string, ScraperClient> ActiveScrapers { get; set; }
    }
}