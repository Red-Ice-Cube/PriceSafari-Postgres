using System.Collections.Generic;
using PriceSafari.Models;

namespace PriceSafari.Models.ViewModels
{
    public class StorePaymentsViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public string LogoUrl { get; set; }
        public string PlanName { get; set; }
        public decimal PlanPrice { get; set; }
        public int ProductsToScrap { get; set; }
        public int ScrapesPerInvoice { get; set; }
        public bool HasUnpaidInvoice { get; set; }
        public bool IsTestPlan { get; set; }
        public List<InvoiceClass> Invoices { get; set; }
        public List<UserPaymentData> PaymentDataList { get; set; }
    }
}
