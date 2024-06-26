using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PriceTracker.Models
{
    public class ScrapHistoryClass
    {
        [Key]
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public int ProductCount { get; set; }
        public int PriceCount { get; set; }
        public int StoreId { get; set; }
        public StoreClass Store { get; set; }
        public ICollection<PriceHistoryClass> PriceHistories { get; set; } = new List<PriceHistoryClass>();
    }
}
