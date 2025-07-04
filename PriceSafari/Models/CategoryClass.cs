using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class CategoryClass
    {
        [Key]
        public int CategoryId { get; set; }
        public int StoreId { get; set; }

        public int Depth { get; set; }
        public string CategoryName { get; set; }
        public string CategoryUrl { get; set; }

        [ValidateNever]
        public StoreClass Store { get; set; }
    }
}
