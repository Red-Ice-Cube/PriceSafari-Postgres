using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using System.Linq;
using System;
using Microsoft.AspNetCore.Mvc.Rendering;
using PriceSafari.Services.Imoje;
using Microsoft.Extensions.Configuration;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class InvoiceController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly IImojeService _imojeService;
        private readonly IConfiguration _configuration;

        public InvoiceController(
            PriceSafariContext context,
            IImojeService imojeService,
            IConfiguration configuration)
        {
            _context = context;
            _imojeService = imojeService;
            _configuration = configuration;
        }

        public async Task<IActionResult> Index()
        {
            var invoices = await _context.Invoices
                .Include(i => i.Store)
                .Include(i => i.Plan)
                .OrderByDescending(i => i.IssueDate.Year)
                .ThenByDescending(i => i.InvoiceNumber)
                .ToListAsync();

            return View("~/Views/ManagerPanel/Invoices/Index.cshtml", invoices);
        }

        public async Task<IActionResult> Create()
        {
            var stores = await _context.Stores.Include(s => s.Plan).ToListAsync();
            ViewBag.Stores = new SelectList(stores, "StoreId", "StoreName");
            var plans = await _context.Plans.ToListAsync();
            ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName");

            return View("~/Views/ManagerPanel/Invoices/Create.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(InvoiceClass invoice)
        {
            if (ModelState.IsValid)
            {
                invoice.IsPaidByCard = false;

                if (invoice.DueDate == null)
                {
                    invoice.DueDate = invoice.IssueDate.AddDays(14);
                }

                _context.Add(invoice);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            var stores = await _context.Stores.Include(s => s.Plan).ToListAsync();
            ViewBag.Stores = new SelectList(stores, "StoreId", "StoreName");
            var plans = await _context.Plans.ToListAsync();
            ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName");

            return View("~/Views/ManagerPanel/Invoices/Create.cshtml", invoice);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var invoice = await _context.Invoices
                .Include(i => i.Store)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.InvoiceId == id);
            if (invoice == null) return NotFound();

            return View("~/Views/ManagerPanel/Invoices/Edit.cshtml", invoice);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, InvoiceClass invoice)
        {
            if (id != invoice.InvoiceId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(invoice);
                    await _context.SaveChangesAsync();

                    if (invoice.IsPaid)
                    {
                        var store = await _context.Stores.Include(s => s.Plan).FirstOrDefaultAsync(s => s.StoreId == invoice.StoreId);
                        if (store != null)
                        {
                            store.ProductsToScrap = store.Plan.ProductsToScrap;
                            await _context.SaveChangesAsync();
                        }
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!InvoiceExists(invoice.InvoiceId)) return NotFound();
                    else throw;
                }

                return RedirectToAction(nameof(Index));
            }

            invoice.Store = await _context.Stores.FindAsync(invoice.StoreId);
            invoice.Plan = await _context.Plans.FindAsync(invoice.PlanId);

            return View("~/Views/ManagerPanel/Invoices/Edit.cshtml", invoice);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsPaid(int id)
        {
            var invoice = await _context.Invoices
                .Include(i => i.Store)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.InvoiceId == id);

            if (invoice == null) return NotFound();

            if (!invoice.IsPaid)
            {
                invoice.IsPaid = true;
                invoice.PaymentDate = DateTime.Now;
                invoice.IsPaidByCard = false;

                if (invoice.InvoiceNumber.StartsWith("FP/"))
                {
                    invoice.OriginalProformaNumber = invoice.InvoiceNumber;
                    int invoiceNumber = await GetNextInvoiceNumberAsync();
                    invoice.InvoiceNumber = $"PS/{invoiceNumber.ToString("D6")}/sDB/{invoice.IssueDate.Year}";
                }

                var store = invoice.Store;
                if (store != null)
                {
                    store.PlanId = invoice.PlanId;
                    store.ProductsToScrap = invoice.Plan.ProductsToScrap;
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<int> GetNextInvoiceNumberAsync()
        {
            var currentYear = DateTime.Now.Year;
            var counter = await _context.InvoiceCounters.FirstOrDefaultAsync(c => c.Year == currentYear);
            if (counter == null)
            {
                counter = new InvoiceCounter { Year = currentYear, LastProformaNumber = 0, LastInvoiceNumber = 0 };
                _context.InvoiceCounters.Add(counter);
                await _context.SaveChangesAsync();
            }

            counter.LastInvoiceNumber++;
            await _context.SaveChangesAsync();
            return counter.LastInvoiceNumber;
        }

        private bool InvoiceExists(int id)
        {
            return _context.Invoices.Any(e => e.InvoiceId == id);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadPdf(int id)
        {
            var invoice = await _context.Invoices
                .Include(i => i.Store)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.InvoiceId == id);

            if (invoice == null) return NotFound("Faktura nie została znaleziona.");

            var logoImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "cid", "signature.png");
            var document = new InvoiceDocument(invoice, logoImagePath);
            var pdfBytes = document.GeneratePdf();

            return File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber.Replace("/", "_")}.pdf");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForceCharge(int id)
        {
            var invoice = await _context.Invoices
                .Include(i => i.Store)
                .FirstOrDefaultAsync(i => i.InvoiceId == id);

            if (invoice == null)
            {
                TempData["Error"] = "Nie znaleziono faktury.";
                return RedirectToAction(nameof(Index));
            }

            if (invoice.IsPaid)
            {
                TempData["Warning"] = "Ta faktura jest już opłacona.";
                return RedirectToAction(nameof(Index));
            }

            if (invoice.Store == null || !invoice.Store.IsRecurringActive || string.IsNullOrEmpty(invoice.Store.ImojePaymentProfileId))
            {
                TempData["Error"] = "Sklep nie posiada aktywnej karty płatniczej.";
                return RedirectToAction(nameof(Index));
            }

            string configIp = _configuration["SERVER_PUBLIC_IP"];
            string usedIp = string.IsNullOrEmpty(configIp) ? "127.0.0.1" : configIp;
            string debugInfo = $"[IP Config: '{configIp}' -> Użyte: '{usedIp}'] [ProfileId: {invoice.Store.ImojePaymentProfileId}]";

            try
            {

                var (success, responseMessage) = await _imojeService.ChargeProfileAsync(invoice.Store.ImojePaymentProfileId, invoice, usedIp);

                if (success)
                {
                    invoice.IsPaid = true;
                    invoice.PaymentDate = DateTime.Now;
                    invoice.IsPaidByCard = true;

                    await _context.SaveChangesAsync();

                    TempData["Success"] = $"SUKCES: Pobrano środki. {debugInfo}";
                }
                else
                {

                    TempData["Error"] = $"BŁĄD PŁATNOŚCI! Imoje odpowiedziało: {responseMessage} \n {debugInfo}";
                }
            }
            catch (Exception ex)
            {
                string innerMsg = ex.InnerException != null ? $" | Inner: {ex.InnerException.Message}" : "";
                TempData["Error"] = $"WYJĄTEK KRYTYCZNY: {ex.Message}{innerMsg}. {debugInfo}";
            }

            return RedirectToAction(nameof(Index));
        }

        //[HttpGet]
        //[AllowAnonymous]
        //public async Task<IActionResult> TestConnectivity()
        //{
        //    var sb = new System.Text.StringBuilder();
        //    sb.AppendLine($"--- ROZSZERZONA DIAGNOSTYKA SIECIOWA [v2.1 - Kompatybilna] ---");
        //    sb.AppendLine($"Czas serwera: {DateTime.Now}");
        //    sb.AppendLine($"System OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        //    sb.AppendLine($"Framework: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        //    sb.AppendLine("------------------------------------------------");

        //    string host = "sandbox.api.imoje.pl";
        //    int port = 443;

        //    try
        //    {

        //        sb.AppendLine($"\n[1. DNS LOOKUP]");
        //        var ipEntry = await System.Net.Dns.GetHostEntryAsync(host);
        //        sb.AppendLine($"   HostName: {ipEntry.HostName}");
        //        foreach (var ip in ipEntry.AddressList)
        //        {
        //            sb.AppendLine($"   -> Adres IP: {ip} (Rodzina: {ip.AddressFamily})");
        //        }

        //        var targetIp = ipEntry.AddressList.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        //        if (targetIp == null)
        //        {
        //            sb.AppendLine("   BŁĄD: Brak adresu IPv4! Kończę test.");
        //            return Content(sb.ToString(), "text/plain");
        //        }

        //        var protocolsToTest = new[]
        //        {
        //    System.Security.Authentication.SslProtocols.Tls12,
        //    System.Security.Authentication.SslProtocols.Tls13
        //};

        //        foreach (var protocol in protocolsToTest)
        //        {
        //            sb.AppendLine($"\n[TESTOWANIE PROTOKOŁU: {protocol}]");
        //            try
        //            {
        //                using (var tcpClient = new System.Net.Sockets.TcpClient())
        //                {
        //                    tcpClient.ReceiveBufferSize = 8192;
        //                    tcpClient.SendBufferSize = 8192;

        //                    sb.Append($"   Łączenie TCP z {targetIp}:{port}... ");
        //                    var connectTask = tcpClient.ConnectAsync(targetIp, port);

        //                    if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
        //                    {
        //                        sb.AppendLine("TIMEOUT TCP!");
        //                        continue;
        //                    }
        //                    await connectTask;
        //                    sb.AppendLine("OK.");

        //                    using (var sslStream = new System.Net.Security.SslStream(tcpClient.GetStream(), false,
        //                        (sender, cert, chain, errors) =>
        //                        {
        //                            sb.AppendLine($"   [Walidacja Certyfikatu Serwera]");
        //                            sb.AppendLine($"     Subject: {cert.Subject}");
        //                            sb.AppendLine($"     Issuer: {cert.Issuer}");
        //                            sb.AppendLine($"     Expiration: {cert.GetExpirationDateString()}");
        //                            sb.AppendLine($"     Błędy SSL: {errors}");
        //                            return true;
        //                        }))
        //                    {
        //                        sb.AppendLine($"   Rozpoczynam SSL Handshake ({protocol})...");

        //                        await sslStream.AuthenticateAsClientAsync(
        //                            host,
        //                            null,
        //                            protocol,
        //                            false
        //                        );

        //                        sb.AppendLine("   >>> SUKCES HANDSHAKE! <<<");
        //                        sb.AppendLine($"   Wynegocjowany Protokół: {sslStream.SslProtocol}");
        //                        sb.AppendLine($"   Szyfr (Cipher): {sslStream.CipherAlgorithm} (Siła: {sslStream.CipherStrength})");
        //                        sb.AppendLine($"   Hash: {sslStream.HashAlgorithm} (Siła: {sslStream.HashStrength})");
        //                        sb.AppendLine($"   KeyExchange: {sslStream.KeyExchangeAlgorithm} (Siła: {sslStream.KeyExchangeStrength})");
        //                        sb.AppendLine($"   IsAuthenticated: {sslStream.IsAuthenticated}");
        //                        sb.AppendLine($"   IsEncrypted: {sslStream.IsEncrypted}");
        //                    }
        //                }
        //            }
        //            catch (System.IO.IOException ioEx)
        //            {
        //                sb.AppendLine("   BŁĄD IO (Zerwanie połączenia):");
        //                sb.AppendLine($"   {ioEx.Message}");
        //                if (ioEx.InnerException != null)
        //                {
        //                    sb.AppendLine($"   Inner: {ioEx.InnerException.GetType().Name}: {ioEx.InnerException.Message}");
        //                    if (ioEx.InnerException is System.Net.Sockets.SocketException sockEx)
        //                    {
        //                        sb.AppendLine($"   SocketErrorCode: {sockEx.SocketErrorCode} (Kod: {sockEx.ErrorCode})");
        //                    }
        //                }
        //            }
        //            catch (System.Security.Authentication.AuthenticationException authEx)
        //            {
        //                sb.AppendLine("   BŁĄD UWIERZYTELNIANIA SSL:");
        //                sb.AppendLine($"   {authEx.Message}");
        //                if (authEx.InnerException != null) sb.AppendLine($"   Inner: {authEx.InnerException.Message}");
        //            }
        //            catch (Exception ex)
        //            {

        //                if (ex.Message.Contains("The requested security protocol is not supported"))
        //                {
        //                    sb.AppendLine($"   INFO: Ten system operacyjny nie obsługuje protokołu {protocol}.");
        //                }
        //                else
        //                {
        //                    sb.AppendLine($"   INNY BŁĄD: {ex.GetType().Name}: {ex.Message}");
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        sb.AppendLine($"\n!!! KRYTYCZNY BŁĄD GŁÓWNY !!!");
        //        sb.AppendLine(ex.ToString());
        //    }

        //    return Content(sb.ToString(), "text/plain");
        //}




        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> TestConnectivity()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- DIAGNOSTYKA OSTATECZNA [v3.0 - SocketsHttpHandler] ---");
            sb.AppendLine($"Czas: {DateTime.Now}");
            sb.AppendLine($"System OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            sb.AppendLine($"Architektura OS: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
            sb.AppendLine($"Framework: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            sb.AppendLine("------------------------------------------------");

            string host = "sandbox.api.imoje.pl";
            int port = 443;
            string testUrl = $"https://{host}/v1/merchant/settings/ips"; // Endpoint testowy

            try
            {
                // --- 1. DNS ---
                sb.AppendLine($"\n[1. DNS LOOKUP]");
                var ipEntry = await System.Net.Dns.GetHostEntryAsync(host);
                foreach (var ip in ipEntry.AddressList)
                {
                    sb.AppendLine($"   -> IP: {ip} ({ip.AddressFamily})");
                }

                var targetIp = ipEntry.AddressList.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (targetIp == null)
                {
                    sb.AppendLine("   BŁĄD: Brak IPv4.");
                    return Content(sb.ToString(), "text/plain");
                }

                // --- 2. NISKOPOZIOMOWY TEST SSLSTREAM (Systemowy Schannel) ---
                var protocolsToTest = new[]
                {
                    System.Security.Authentication.SslProtocols.Tls12,
                    System.Security.Authentication.SslProtocols.Tls13
                };

                foreach (var protocol in protocolsToTest)
                {
                    sb.AppendLine($"\n[2. TEST SYSTEMOWY: {protocol}]");
                    try
                    {
                        using (var tcpClient = new System.Net.Sockets.TcpClient())
                        {
                            await tcpClient.ConnectAsync(targetIp, port);
                            using (var sslStream = new System.Net.Security.SslStream(tcpClient.GetStream(), false, (s, c, ch, e) => true))
                            {
                                sb.Append($"   Próba handshake {protocol}... ");
                                await sslStream.AuthenticateAsClientAsync(host, null, protocol, false);
                                sb.AppendLine("SUKCES!");
                                sb.AppendLine($"   Szyfr: {sslStream.CipherAlgorithm} | Protokół: {sslStream.SslProtocol}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"   BŁĄD: {ex.Message}");
                        if (ex.InnerException != null) sb.AppendLine($"   Inner: {ex.InnerException.Message}");
                    }
                }

                // --- 3. NOWOCZESNY TEST .NET 9 (SocketsHttpHandler - To co sugeruje Webio) ---
                sb.AppendLine($"\n[3. TEST .NET 9 HTTP CLIENT (Wymuszenie TLS 1.3)]");
                sb.AppendLine("   Testujemy czy SocketsHttpHandler potrafi wymusić TLS 1.3 na tym systemie...");

                try
                {
                    var handler = new SocketsHttpHandler();
                    handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                    {
                        // WYMUSZAMY TYLKO TLS 1.3 - To kluczowy test
                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls13,
                        RemoteCertificateValidationCallback = (s, c, ch, e) => true // Ignoruj błędy certyfikatu
                    };
                    handler.ConnectTimeout = TimeSpan.FromSeconds(5);

                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(5);
                        client.DefaultRequestHeaders.Add("User-Agent", "TestAgent");

                        sb.Append("   Wysyłam GET... ");
                        var response = await client.GetAsync(testUrl);
                        sb.AppendLine($"SUKCES! Status: {response.StatusCode}");
                    }
                }
                catch (PlatformNotSupportedException platEx)
                {
                    sb.AppendLine("\n   !!! DOWÓD DLA WEBIO !!!");
                    sb.AppendLine($"   BŁĄD: PlatformNotSupportedException");
                    sb.AppendLine($"   Treść: {platEx.Message}");
                    sb.AppendLine("   WNIOSEK: Ten system operacyjny NIE WSPIERA TLS 1.3 w .NET 9.");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"   BŁĄD: {ex.GetType().Name}");
                    sb.AppendLine($"   Treść: {ex.Message}");
                    if (ex.InnerException != null) sb.AppendLine($"   Inner: {ex.InnerException.Message}");
                }

                // --- 4. TEST MIESZANY (To co sugerowali na StackOverflow) ---
                sb.AppendLine($"\n[4. TEST .NET 9 (Mieszany Tls12 | Tls13)]");
                try
                {
                    var handler = new SocketsHttpHandler();
                    handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                    {
                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                        RemoteCertificateValidationCallback = (s, c, ch, e) => true
                    };

                    using (var client = new HttpClient(handler))
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "TestAgent");
                        sb.Append("   Wysyłam GET... ");
                        var response = await client.GetAsync(testUrl);
                        sb.AppendLine($"SUKCES! Status: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"   BŁĄD: {ex.Message}");
                    if (ex.Message.Contains("EOF"))
                    {
                        sb.AppendLine("   INTERPRETACJA: Serwer (My) wysłał TLS 1.2, Bank odrzucił połączenie (EOF).");
                    }
                }

            }
            catch (Exception ex)
            {
                sb.AppendLine($"\n!!! KRYTYCZNY BŁĄD SKRYPTU !!!\n{ex}");
            }

            return Content(sb.ToString(), "text/plain");
        }


    }
}