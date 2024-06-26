using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class Category
    {
        [Key]
        public int CategoryId { get; set; }

        public string CategoryName { get; set; }
        public int Validation { get; set; }
        public decimal? CommissionPercentage { get; set; }
        public string CodeCAT { get; set; }

        private static readonly Random random = new Random();
        private const string pool = "QWERTYUIOPASDFGHJKLZXCVBNMqwertyuiopasdfghjklzxcvbnm1234567890";

        public Category()
        {
            CodeCAT = GenerateUniqueCodeCAT();
        }

        private string GenerateUniqueCodeCAT()
        {
            var length = 5;
            string result;
            lock (random)
            {
                var chars = Enumerable
                    .Repeat(0, length)
                    .Select(x => pool[random.Next(0, pool.Length)]);
                result = new string(chars.ToArray());
            }
            return result;
        }

        public bool IsDeleted { get; set; } = false;

        public int? StoreId { get; set; }

        public bool CodeTracking { get; set; }

        [ForeignKey("StoreId")]
        public Store? Store { get; set; }

        public ICollection<Generator>? Generator { get; set; }
        public ICollection<Product>? Product { get; set; }
    }
}