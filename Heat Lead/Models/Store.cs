using System.ComponentModel.DataAnnotations;

namespace Heat_Lead.Models
{
    public class Store
    {
        [Key]
        public int StoreId { get; set; }

        public string StoreName { get; set; }

        public string CodeSTO { get; set; }

        public string APIurl { get; set; }
        public string APIkey { get; set; }
        public string? LogoUrl { get; set; }

        private static readonly Random random = new Random();
        private const string pool = "QWERTYUIOPASDFGHJKLZXCVBNMqwertyuiopasdfghjklzxcvbnm1234567890";

        public Store()
        {
            CodeSTO = GenerateUniqueCodeSTO();
        }

        private string GenerateUniqueCodeSTO()
        {
            var length = 3;
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

        public ICollection<Category>? Category { get; set; }
    }
}