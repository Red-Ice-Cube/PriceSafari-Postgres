namespace Heat_Lead.Models.ManagerViewModels
{
    public class ManagerPayoutViewModel
    {
        public List<PendingPayoutsViewModel> PendingPayouts { get; set; }
        public List<DonePayoutsViewModel> DonePayouts { get; set; }
        public List<PayoutDetailsViewModel> PayoutDetails { get; set; }
        public List<PayoutHistoryViewModel> PayoutHistory { get; set; }

        public class PayoutDetailsViewModel
        {
            public int PaycheckId { get; set; }
            public string PartnerName { get; set; }
            public string PartnerSurname { get; set; }
            public string PartnerEmail { get; set; }
            public string Address { get; set; }
            public string City { get; set; }
            public string PostalCode { get; set; }
            public string? Pesel { get; set; }
            public string? TaxNumber { get; set; }
            public string BankAccountNumber { get; set; }
            public string? TaxOffice { get; set; }
            public string? CompanyName { get; set; }
            public decimal Amount { get; set; }

            public DateTime CreationDate { get; set; }

            public bool MoneySended { get; set; }
            public bool IsCompany { get; set; }
        }

        public class PayoutHistoryViewModel
        {
            public int PaycheckId { get; set; }
            public string PartnerName { get; set; }
            public string PartnerSurname { get; set; }
            public string PartnerEmail { get; set; }
            public string Address { get; set; }
            public string City { get; set; }
            public string PostalCode { get; set; }
            public string? Pesel { get; set; }
            public string? CompanyName { get; set; }
            public string BankAccountNumber { get; set; }
            public string? TaxOffice { get; set; }
            public string? TaxNumber { get; set; }
            public decimal Amount { get; set; }

            public DateTime CreationDate { get; set; }

            public bool MoneySended { get; set; }
            public bool IsCompany { get; set; }
        }

        public class PendingPayoutsViewModel
        {
            public int PaycheckId { get; set; }
            public string PartnerName { get; set; }
            public string PartnerSurname { get; set; }

            public decimal Amount { get; set; }

            public DateTime CreationDate { get; set; }

            public bool MoneySended { get; set; }
            public bool IsCompany { get; set; }
        }

        public class DonePayoutsViewModel
        {
            public int PaycheckId { get; set; }
            public string PartnerName { get; set; }
            public string PartnerSurname { get; set; }

            public decimal Amount { get; set; }

            public DateTime CreationDate { get; set; }

            public bool MoneySended { get; set; }
            public bool IsCompany { get; set; }
        }
    }
}