using System.ComponentModel.DataAnnotations;

public class UserPaymentDataViewModel
{
    public int? PaymentDataId { get; set; } // Nullable to handle new entries
    [Required]
    [Display(Name = "Nazwa firmy")]
    public string CompanyName { get; set; }

    [Required]
    [Display(Name = "Adres")]
    public string Address { get; set; }

    [Required]
    [Display(Name = "Kod pocztowy")]
    public string PostalCode { get; set; }

    [Required]
    [Display(Name = "Miasto")]
    public string City { get; set; }

    [Required]
    [Display(Name = "NIP")]
    public string NIP { get; set; }
}
