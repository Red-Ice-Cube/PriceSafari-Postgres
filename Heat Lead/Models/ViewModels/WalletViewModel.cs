using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Heat_Lead.Models.ViewModels
{
    public class WalletViewModel
    {
        public decimal InValidationEarnings { get; set; }
        public decimal ReadyEarnings { get; set; }
        public decimal PaidEarnings { get; set; }
        public decimal MinimumPayout { get; set; }
        public WithdrawViewModel WithdrawViewModel { get; set; }
    }

    public class WithdrawViewModel : IValidatableObject
    {
        [Required(ErrorMessage = "Musisz zaakceptować regulamin, aby kontynuować.")]
        [Display(Name = "Akceptuję regulamin programu partnerskiego")]
        public bool AcceptsTerms { get; set; }

        [Required(ErrorMessage = "Kwota wypłaty jest wymagana.")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Adres jest wymagany.")]
        public string Address { get; set; }

        [Required(ErrorMessage = "Miasto jest wymagane.")]
        public string City { get; set; }

        [Required(ErrorMessage = "Kod pocztowy jest wymagany.")]
        public string PostalCode { get; set; }

        public bool IsCompany { get; set; } = false;

        public string? CompanyTaxNumber { get; set; }
        public string? CompanyName { get; set; }

        public string? Pesel { get; set; }

        public string? TaxOffice { get; set; }

        [Required(ErrorMessage = "Numer konta bankowego jest wymagany.")]
        [RegularExpression(@"^\d{26}$", ErrorMessage = "Numer konta bankowego musi składać się z 26 cyfr.")]
        public string BankAccountNumber { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (IsCompany)
            {
                if (string.IsNullOrWhiteSpace(CompanyTaxNumber) || !System.Text.RegularExpressions.Regex.IsMatch(CompanyTaxNumber, @"^\d{10}$"))
                {
                    yield return new ValidationResult("NIP firmy jest wymagany.", new[] { nameof(CompanyTaxNumber) });
                }

                if (string.IsNullOrWhiteSpace(CompanyName))
                {
                    yield return new ValidationResult("Nazwa firmy jest wymagana.", new[] { nameof(CompanyName) });
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Pesel) || !System.Text.RegularExpressions.Regex.IsMatch(Pesel, @"^\d{11}$"))
                {
                    yield return new ValidationResult("Numer PESEL musi składać się z 11 cyfr.", new[] { nameof(Pesel) });
                }

                if (string.IsNullOrWhiteSpace(TaxOffice))
                {
                    yield return new ValidationResult("Nazwa urzędu skarbowego jest wymagana.", new[] { nameof(TaxOffice) });
                }
            }
        }
    }
}