using System.Text;
using System.Xml;
using PriceSafari.Models;
using System.Globalization;
namespace PriceSafari.Services.KSeF
{
    public interface IInvoiceXmlBuilder
    {

        string BuildInvoiceXml(InvoiceClass invoice);
    }

    public class InvoiceXmlBuilder : IInvoiceXmlBuilder
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<InvoiceXmlBuilder> _logger;

        private const string SELLER_NAME = "HEATED BOX POLSKA SP. Z O.O.";
        private const string SELLER_ADDRESS = "Wojciecha Korfantego 16";

        private const string SELLER_POSTAL_CODE = "42-202";

        private const string SELLER_CITY = "Częstochowa";

        private const string SELLER_COUNTRY = "PL";

        public InvoiceXmlBuilder(IConfiguration configuration, ILogger<InvoiceXmlBuilder> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public string BuildInvoiceXml(InvoiceClass invoice)
        {
            var sellerNip = Environment.GetEnvironmentVariable("KSEF_NIP")
                ?? throw new InvalidOperationException("Brak KSEF_NIP w zmiennych środowiskowych");

            decimal netAmount = invoice.NetAmount;
            decimal vatRate = 23m;
            decimal vatAmount = Math.Round(netAmount * (vatRate / 100m), 2);
            decimal grossAmount = netAmount + vatAmount;

            string ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";

            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(false),

                OmitXmlDeclaration = false
            };

            using var stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("Faktura", ns);

                writer.WriteStartElement("Naglowek");
                {
                    writer.WriteStartElement("KodFormularza");
                    writer.WriteAttributeString("kodSystemowy", "FA (3)");
                    writer.WriteAttributeString("wersjaSchemy", "1-0E");
                    writer.WriteString("FA");
                    writer.WriteEndElement();

                    writer.WriteElementString("WariantFormularza", "3");
                    writer.WriteElementString("DataWytworzeniaFa",
                        invoice.IssueDate.ToString("yyyy-MM-ddTHH:mm:ss"));
                    writer.WriteElementString("SystemInfo", "PriceSafari");
                }
                writer.WriteEndElement();

                writer.WriteStartElement("Podmiot1");
                {
                    writer.WriteStartElement("DaneIdentyfikacyjne");
                    writer.WriteElementString("NIP", sellerNip);
                    writer.WriteElementString("Nazwa", SELLER_NAME);
                    writer.WriteEndElement();

                    writer.WriteStartElement("Adres");
                    writer.WriteElementString("KodKraju", SELLER_COUNTRY);
                    writer.WriteElementString("AdresL1", SELLER_ADDRESS);
                    writer.WriteElementString("AdresL2", $"{SELLER_POSTAL_CODE} {SELLER_CITY}");
                    writer.WriteEndElement();

                }
                writer.WriteEndElement();

                writer.WriteStartElement("Podmiot2");
                {
                    writer.WriteStartElement("DaneIdentyfikacyjne");

                    if (!string.IsNullOrWhiteSpace(invoice.NIP))
                    {
                        writer.WriteElementString("NIP", invoice.NIP.Replace("-", "").Trim());
                    }
                    else
                    {
                        writer.WriteElementString("BrakID", "1");
                    }

                    writer.WriteElementString("Nazwa",
                        !string.IsNullOrWhiteSpace(invoice.CompanyName)
                            ? invoice.CompanyName
                            : "Brak Danych");
                    writer.WriteEndElement();

                    writer.WriteStartElement("Adres");
                    writer.WriteElementString("KodKraju", "PL");
                    writer.WriteElementString("AdresL1",
                        !string.IsNullOrWhiteSpace(invoice.Address)
                            ? invoice.Address
                            : "Brak");

                    string adresL2 = $"{invoice.PostalCode ?? ""} {invoice.City ?? ""}".Trim();
                    if (!string.IsNullOrWhiteSpace(adresL2))
                    {
                        writer.WriteElementString("AdresL2", adresL2);
                    }
                    writer.WriteEndElement();

                    writer.WriteElementString("JST", "2");

                    writer.WriteElementString("GV", "2");

                }
                writer.WriteEndElement();

                writer.WriteStartElement("Fa");
                {
                    writer.WriteElementString("KodWaluty", "PLN");
                    writer.WriteElementString("P_1", invoice.IssueDate.ToString("yyyy-MM-dd"));

                    writer.WriteElementString("P_1M", SELLER_CITY);

                    writer.WriteElementString("P_2", invoice.InvoiceNumber);

                    writer.WriteStartElement("OkresFa");
                    {
                        writer.WriteElementString("P_6_Od", invoice.IssueDate.ToString("yyyy-MM-dd"));
                        var endDate = invoice.IssueDate.AddDays(invoice.DaysIncluded);
                        writer.WriteElementString("P_6_Do", endDate.ToString("yyyy-MM-dd"));
                    }
                    writer.WriteEndElement();

                    writer.WriteElementString("P_13_1", netAmount.ToString("F2", CultureInfo.InvariantCulture));
                    writer.WriteElementString("P_14_1", vatAmount.ToString("F2", CultureInfo.InvariantCulture));
                    writer.WriteElementString("P_15", grossAmount.ToString("F2", CultureInfo.InvariantCulture));

                    writer.WriteStartElement("Adnotacje");
                    {
                        writer.WriteElementString("P_16", "2");

                        writer.WriteElementString("P_17", "2");

                        writer.WriteElementString("P_18", "2");

                        writer.WriteElementString("P_18A", "2");

                        writer.WriteStartElement("Zwolnienie");
                        writer.WriteElementString("P_19N", "1");

                        writer.WriteEndElement();

                        writer.WriteStartElement("NoweSrodkiTransportu");
                        writer.WriteElementString("P_22N", "1");

                        writer.WriteEndElement();

                        writer.WriteElementString("P_23", "2");

                        writer.WriteStartElement("PMarzy");
                        writer.WriteElementString("P_PMarzyN", "1");

                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();

                    writer.WriteElementString("RodzajFaktury", "VAT");

                    writer.WriteStartElement("FaWiersz");
                    {
                        writer.WriteElementString("NrWierszaFa", "1");

                        var plan = invoice.Plan;
                        string fullServiceName = $"Dostęp do platformy PriceSafari - Monitoring i Automatyzacja cen | " +
                                                 $"{(plan?.PlanName ?? "Plan Standard")} | " +
                                                 $"{invoice.DaysIncluded} dni | " +
                                                 $"Max. Marketplace SKU - {(plan?.ProductsToScrapAllegro?.ToString() ?? "0")} | " +
                                                 $"Max. Price Comparison SKU - {(plan?.ProductsToScrap?.ToString() ?? "0")}";


                        if (!string.IsNullOrWhiteSpace(plan?.Info))
                        {
                            fullServiceName += $" | {plan.Info}";
                        }

                        writer.WriteElementString("P_7", fullServiceName);

                        writer.WriteElementString("P_8A", "szt.");
                        writer.WriteElementString("P_8B", "1");
                        writer.WriteElementString("P_9A", netAmount.ToString("F2", CultureInfo.InvariantCulture));
                        writer.WriteElementString("P_11", netAmount.ToString("F2", CultureInfo.InvariantCulture));
                        writer.WriteElementString("P_12", "23");
                    }
                    writer.WriteEndElement();

                    if (invoice.DueDate.HasValue)
                    {
                        writer.WriteStartElement("Platnosc");
                        {
                            writer.WriteStartElement("TerminPlatnosci");
                            writer.WriteElementString("Termin", invoice.DueDate.Value.ToString("yyyy-MM-dd"));
                            writer.WriteEndElement();

                            writer.WriteElementString("FormaPlatnosci", invoice.IsPaidByCard ? "4" : "6");

                            string myRawAccountNumber = "47 1050 1142 1000 0090 8605 6679";
                            string cleanAccountNumber = myRawAccountNumber.Replace(" ", "").Replace("PL", "");
                            string mySwiftCode = "INGBPLPW";

                            if (!invoice.IsPaidByCard)
                            {
                                writer.WriteStartElement("RachunekBankowy");
                                {

                                    writer.WriteElementString("NrRB", cleanAccountNumber);

                                    writer.WriteElementString("SWIFT", mySwiftCode);

                                    writer.WriteElementString("NazwaBanku", "ING Bank Śląski S.A.");

                                    writer.WriteElementString("OpisRachunku", "Rachunek do wpłat PriceSafari");
                                }
                                writer.WriteEndElement();

                            }
                        }
                        writer.WriteEndElement();

                    }

                }

                writer.WriteEndElement();

                writer.WriteStartElement("Stopka");
                {
                    writer.WriteStartElement("Rejestry");
                    {
                        writer.WriteElementString("KRS", "0000897972");
                        writer.WriteElementString("REGON", "388799620");
                        writer.WriteElementString("BDO", "000555719");
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();

                writer.WriteEndElement();

                writer.WriteEndDocument();
            }

            var xml = Encoding.UTF8.GetString(stream.ToArray());

            _logger.LogDebug($"Wygenerowano XML FA(3) dla FV: {invoice.InvoiceNumber} ({xml.Length} bytes)");

            return xml;
        }
    }
}