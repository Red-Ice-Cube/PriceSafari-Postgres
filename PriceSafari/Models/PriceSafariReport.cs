using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class PriceSafariReport
    {
        [Key]
        public int ReportId { get; set; }

        public string ReportName { get; set; }

        public int StoreId { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public List<int>? ProductIds { get; set; } = new List<int>();

        public List<int> RegionIds { get; set; } = new List<int>();

        public bool? Prepared { get; set; }

    }
}
