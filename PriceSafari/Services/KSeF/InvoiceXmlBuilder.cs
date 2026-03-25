using System.Text;
using System.Xml;
using PriceSafari.Models;

namespace PriceSafari.Services.KSeF
{
    public interface IInvoiceXmlBuilder
    {
        /// <summary>
        /// Buduje XML faktury w formacie FA(3) na podstawie InvoiceClass.
        /// </summary>
        string BuildInvoiceXml(InvoiceClass invoice);
    }

    public class InvoiceXmlBuilder : IInvoiceXmlBuilder
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<InvoiceXmlBuilder> _logger;

        // Dane sprzedawcy — Heated Box Sp. z o.o.
        // Możesz przenieść do .env jeśli wolisz
        private const string SELLER_NAME = "HEATED BOX POLSKA SP. Z O.O.";
        private const string SELLER_ADDRESS = "Wojciecha Korfantego 16";         // TODO: uzupełnij
        private const string SELLER_POSTAL_CODE = "42-202";          // TODO: uzupełnij
        private const string SELLER_CITY = "Częstochowa";                // TODO: uzupełnij
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

            // Obliczenia kwot
            decimal netAmount = invoice.NetAmount;
            decimal vatRate = 23m;
            decimal vatAmount = Math.Round(netAmount * (vatRate / 100m), 2);
            decimal grossAmount = netAmount + vatAmount;

            // Namespace FA(3) — schema opublikowana 25.06.2025, nr wzoru 13775
            string ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";

            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(false), // bez BOM
                OmitXmlDeclaration = false
            };

            using var stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("Faktura", ns);

                // === NAGLOWEK ===
                writer.WriteStartElement("Naglowek");
                {
                    writer.WriteStartElement("KodFormularza");
                    writer.WriteAttributeString("kodSystemowy", "FA (3)");
                    writer.WriteAttributeString("wersjaSchemy", "1-0E");
                    writer.WriteString("FA");
                    writer.WriteEndElement(); // KodFormularza

                    writer.WriteElementString("WariantFormularza", "3");
                    writer.WriteElementString("DataWytworzeniaFa",
                        invoice.IssueDate.ToString("yyyy-MM-ddTHH:mm:ss"));
                    writer.WriteElementString("SystemInfo", "PriceSafari");
                }
                writer.WriteEndElement(); // Naglowek

                // === PODMIOT1 — Sprzedawca (Heated Box) ===
                writer.WriteStartElement("Podmiot1");
                {
                    writer.WriteStartElement("DaneIdentyfikacyjne");
                    writer.WriteElementString("NIP", sellerNip);
                    writer.WriteElementString("Nazwa", SELLER_NAME);
                    writer.WriteEndElement(); // DaneIdentyfikacyjne

                    writer.WriteStartElement("Adres");
                    writer.WriteElementString("KodKraju", SELLER_COUNTRY);
                    writer.WriteElementString("AdresL1", SELLER_ADDRESS);
                    writer.WriteElementString("AdresL2", $"{SELLER_POSTAL_CODE} {SELLER_CITY}");
                    writer.WriteEndElement(); // Adres
                }
                writer.WriteEndElement(); // Podmiot1

                // === PODMIOT2 — Nabywca (Klient ze sklepu) ===
                writer.WriteStartElement("Podmiot2");
                {
                    writer.WriteStartElement("DaneIdentyfikacyjne");
                    if (!string.IsNullOrWhiteSpace(invoice.NIP))
                    {
                        writer.WriteElementString("NIP", invoice.NIP.Replace("-", "").Trim());
                    }
                    writer.WriteElementString("Nazwa",
                        !string.IsNullOrWhiteSpace(invoice.CompanyName)
                            ? invoice.CompanyName
                            : "Brak Danych");
                    writer.WriteEndElement(); // DaneIdentyfikacyjne

                    writer.WriteStartElement("Adres");
                    writer.WriteElementString("KodKraju", "PL");
                    writer.WriteElementString("AdresL1",
                        !string.IsNullOrWhiteSpace(invoice.Address)
                            ? invoice.Address
                            : "Brak");
                    writer.WriteElementString("AdresL2",
                        $"{invoice.PostalCode ?? ""} {invoice.City ?? ""}".Trim());
                    writer.WriteEndElement(); // Adres
                }
                writer.WriteEndElement(); // Podmiot2

                // === FA — Dane faktury ===
                writer.WriteStartElement("Fa");
                {
                    writer.WriteElementString("KodWaluty", "PLN");
                    writer.WriteElementString("P_1", invoice.IssueDate.ToString("yyyy-MM-dd"));
                    writer.WriteElementString("P_2", invoice.InvoiceNumber);

                    // Sumy wg stawek VAT
                    // P_13_1 = suma netto w stawce 23%
                    writer.WriteElementString("P_13_1", netAmount.ToString("F2"));
                    // P_14_1 = suma VAT w stawce 23%
                    writer.WriteElementString("P_14_1", vatAmount.ToString("F2"));
                    // P_15 = łączna kwota brutto
                    writer.WriteElementString("P_15", grossAmount.ToString("F2"));

                    // === WIERSZ FAKTURY ===
                    writer.WriteStartElement("FaWiersz");
                    {
                        writer.WriteElementString("NrWierszaFa", "1");

                        // Nazwa usługi — łączymy z nazwą planu
                        string serviceName = invoice.Plan != null
                            ? $"Usługa monitoringu cen - PriceSafari ({invoice.Plan.PlanName})"
                            : "Usługa monitoringu cen - PriceSafari";
                        writer.WriteElementString("P_7", serviceName);

                        writer.WriteElementString("P_8A", "szt.");  // jednostka miary
                        writer.WriteElementString("P_8B", "1");     // ilość
                        writer.WriteElementString("P_9A", netAmount.ToString("F2")); // cena jednostkowa netto
                        writer.WriteElementString("P_11", netAmount.ToString("F2")); // wartość netto wiersza
                        writer.WriteElementString("P_12", "23");    // stawka VAT
                    }
                    writer.WriteEndElement(); // FaWiersz

                    // Jeśli mamy rabat, dodajemy drugi wiersz informacyjny
                    // (opcjonalne — rabat jest już wliczony w NetAmount)

                    // === ADNOTACJE (wymagane) ===
                    writer.WriteStartElement("Adnotacje");
                    {
                        writer.WriteElementString("P_16", "2"); // 2 = nie stosuje metody kasowej
                        writer.WriteElementString("P_17", "2"); // 2 = nie jest samofakturowaniem
                        writer.WriteElementString("P_18", "2"); // 2 = nie jest RR
                        writer.WriteElementString("P_18A", "2"); // 2 = nie stosuje MPP

                        writer.WriteStartElement("Zwolnienie");
                        writer.WriteElementString("P_19N", "1"); // 1 = nie korzysta ze zwolnienia
                        writer.WriteEndElement(); // Zwolnienie
                    }
                    writer.WriteEndElement(); // Adnotacje

                    // === PŁATNOŚĆ ===
                    if (invoice.DueDate.HasValue)
                    {
                        writer.WriteStartElement("Platnosc");
                        {
                            writer.WriteStartElement("TerminPlatnosci");
                            writer.WriteElementString("Termin",
                                invoice.DueDate.Value.ToString("yyyy-MM-dd"));
                            writer.WriteEndElement(); // TerminPlatnosci

                            // Forma płatności
                            writer.WriteStartElement("FormaPlatnosci");
                            if (invoice.IsPaidByCard)
                            {
                                writer.WriteElementString("FormaPlatnosci", "4"); // 4 = karta
                            }
                            else
                            {
                                writer.WriteElementString("FormaPlatnosci", "6"); // 6 = przelew
                            }
                            writer.WriteEndElement(); // FormaPlatnosci
                        }
                        writer.WriteEndElement(); // Platnosc
                    }

                    writer.WriteElementString("RodzajFaktury", "VAT");
                }
                writer.WriteEndElement(); // Fa

                writer.WriteEndElement(); // Faktura
                writer.WriteEndDocument();
            }

            var xml = Encoding.UTF8.GetString(stream.ToArray());

            _logger.LogDebug($"Wygenerowano XML FA(3) dla FV: {invoice.InvoiceNumber} ({xml.Length} bytes)");

            return xml;
        }
    }
}