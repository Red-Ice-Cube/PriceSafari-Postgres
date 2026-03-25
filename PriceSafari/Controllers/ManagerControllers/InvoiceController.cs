//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using Microsoft.AspNetCore.Authorization;
//using System.Threading.Tasks;
//using System.Linq;
//using System;
//using Microsoft.AspNetCore.Mvc.Rendering;
//using PriceSafari.Services.Imoje;
//using Microsoft.Extensions.Configuration;

//namespace PriceSafari.Controllers.ManagerControllers
//{
//    [Authorize(Roles = "Admin")]
//    public class InvoiceController : Controller
//    {
//        private readonly PriceSafariContext _context;
//        private readonly IImojeService _imojeService;
//        private readonly IConfiguration _configuration;

//        public InvoiceController(
//            PriceSafariContext context,
//            IImojeService imojeService,
//            IConfiguration configuration)
//        {
//            _context = context;
//            _imojeService = imojeService;
//            _configuration = configuration;
//        }

//        public async Task<IActionResult> Index()
//        {
//            var invoices = await _context.Invoices
//                .Include(i => i.Store)
//                .Include(i => i.Plan)
//                .OrderByDescending(i => i.IssueDate.Year)
//                .ThenByDescending(i => i.InvoiceNumber)
//                .ToListAsync();

//            return View("~/Views/ManagerPanel/Invoices/Index.cshtml", invoices);
//        }

//        public async Task<IActionResult> Create()
//        {
//            var stores = await _context.Stores.Include(s => s.Plan).ToListAsync();
//            ViewBag.Stores = new SelectList(stores, "StoreId", "StoreName");
//            var plans = await _context.Plans.ToListAsync();
//            ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName");

//            return View("~/Views/ManagerPanel/Invoices/Create.cshtml");
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> Create(InvoiceClass invoice)
//        {
//            if (ModelState.IsValid)
//            {
//                invoice.IsPaidByCard = false;

//                if (invoice.DueDate == null)
//                {
//                    invoice.DueDate = invoice.IssueDate.AddDays(14);
//                }

//                _context.Add(invoice);
//                await _context.SaveChangesAsync();
//                return RedirectToAction(nameof(Index));
//            }

//            var stores = await _context.Stores.Include(s => s.Plan).ToListAsync();
//            ViewBag.Stores = new SelectList(stores, "StoreId", "StoreName");
//            var plans = await _context.Plans.ToListAsync();
//            ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName");

//            return View("~/Views/ManagerPanel/Invoices/Create.cshtml", invoice);
//        }

//        public async Task<IActionResult> Edit(int? id)
//        {
//            if (id == null) return NotFound();

//            var invoice = await _context.Invoices
//                .Include(i => i.Store)
//                .Include(i => i.Plan)
//                .FirstOrDefaultAsync(i => i.InvoiceId == id);
//            if (invoice == null) return NotFound();

//            return View("~/Views/ManagerPanel/Invoices/Edit.cshtml", invoice);
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> Edit(int id, InvoiceClass invoice)
//        {
//            if (id != invoice.InvoiceId) return NotFound();

//            if (ModelState.IsValid)
//            {
//                try
//                {
//                    _context.Update(invoice);
//                    await _context.SaveChangesAsync();

//                    if (invoice.IsPaid)
//                    {
//                        var store = await _context.Stores.Include(s => s.Plan).FirstOrDefaultAsync(s => s.StoreId == invoice.StoreId);
//                        if (store != null)
//                        {
//                            store.ProductsToScrap = store.Plan.ProductsToScrap;
//                            await _context.SaveChangesAsync();
//                        }
//                    }
//                }
//                catch (DbUpdateConcurrencyException)
//                {
//                    if (!InvoiceExists(invoice.InvoiceId)) return NotFound();
//                    else throw;
//                }

//                return RedirectToAction(nameof(Index));
//            }

//            invoice.Store = await _context.Stores.FindAsync(invoice.StoreId);
//            invoice.Plan = await _context.Plans.FindAsync(invoice.PlanId);

//            return View("~/Views/ManagerPanel/Invoices/Edit.cshtml", invoice);
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> MarkAsPaid(int id)
//        {
//            var invoice = await _context.Invoices
//                .Include(i => i.Store)
//                .Include(i => i.Plan)
//                .FirstOrDefaultAsync(i => i.InvoiceId == id);

//            if (invoice == null) return NotFound();

//            if (!invoice.IsPaid)
//            {
//                invoice.IsPaid = true;
//                invoice.PaymentDate = DateTime.Now;
//                invoice.IsPaidByCard = false;

//                if (invoice.InvoiceNumber.StartsWith("FP/"))
//                {
//                    invoice.OriginalProformaNumber = invoice.InvoiceNumber;
//                    int invoiceNumber = await GetNextInvoiceNumberAsync();
//                    invoice.InvoiceNumber = $"PS/{invoiceNumber.ToString("D6")}/sDB/{invoice.IssueDate.Year}";
//                }

//                var store = invoice.Store;
//                if (store != null)
//                {
//                    store.PlanId = invoice.PlanId;
//                    store.ProductsToScrap = invoice.Plan.ProductsToScrap;
//                }

//                await _context.SaveChangesAsync();
//            }

//            return RedirectToAction(nameof(Index));
//        }

//        private async Task<int> GetNextInvoiceNumberAsync()
//        {
//            var currentYear = DateTime.Now.Year;
//            var counter = await _context.InvoiceCounters.FirstOrDefaultAsync(c => c.Year == currentYear);
//            if (counter == null)
//            {
//                counter = new InvoiceCounter { Year = currentYear, LastProformaNumber = 0, LastInvoiceNumber = 0 };
//                _context.InvoiceCounters.Add(counter);
//                await _context.SaveChangesAsync();
//            }

//            counter.LastInvoiceNumber++;
//            await _context.SaveChangesAsync();
//            return counter.LastInvoiceNumber;
//        }

//        private bool InvoiceExists(int id)
//        {
//            return _context.Invoices.Any(e => e.InvoiceId == id);
//        }

//        [HttpGet]
//        public async Task<IActionResult> DownloadPdf(int id)
//        {
//            var invoice = await _context.Invoices
//                .Include(i => i.Store)
//                .Include(i => i.Plan)
//                .FirstOrDefaultAsync(i => i.InvoiceId == id);

//            if (invoice == null) return NotFound("Faktura nie została znaleziona.");

//            var logoImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "cid", "signature.png");
//            var document = new InvoiceDocument(invoice, logoImagePath);
//            var pdfBytes = document.GeneratePdf();

//            return File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber.Replace("/", "_")}.pdf");
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> ForceCharge(int id)
//        {
//            var invoice = await _context.Invoices
//                .Include(i => i.Store)
//                .FirstOrDefaultAsync(i => i.InvoiceId == id);

//            if (invoice == null)
//            {
//                TempData["Error"] = "Nie znaleziono faktury.";
//                return RedirectToAction(nameof(Index));
//            }

//            if (invoice.IsPaid)
//            {
//                TempData["Warning"] = "Ta faktura jest już opłacona.";
//                return RedirectToAction(nameof(Index));
//            }

//            if (invoice.Store == null || !invoice.Store.IsRecurringActive || string.IsNullOrEmpty(invoice.Store.ImojePaymentProfileId))
//            {
//                TempData["Error"] = "Sklep nie posiada aktywnej karty płatniczej.";
//                return RedirectToAction(nameof(Index));
//            }

//            string configIp = _configuration["SERVER_PUBLIC_IP"];
//            string usedIp = string.IsNullOrEmpty(configIp) ? "127.0.0.1" : configIp;
//            string debugInfo = $"[IP Config: '{configIp}' -> Użyte: '{usedIp}'] [ProfileId: {invoice.Store.ImojePaymentProfileId}]";

//            try
//            {

//                var (success, responseMessage) = await _imojeService.ChargeProfileAsync(invoice.Store.ImojePaymentProfileId, invoice, usedIp);

//                if (success)
//                {
//                    invoice.IsPaid = true;
//                    invoice.PaymentDate = DateTime.Now;
//                    invoice.IsPaidByCard = true;

//                    await _context.SaveChangesAsync();

//                    TempData["Success"] = $"SUKCES: Pobrano środki. {debugInfo}";
//                }
//                else
//                {

//                    TempData["Error"] = $"BŁĄD PŁATNOŚCI! Imoje odpowiedziało: {responseMessage} \n {debugInfo}";
//                }
//            }
//            catch (Exception ex)
//            {
//                string innerMsg = ex.InnerException != null ? $" | Inner: {ex.InnerException.Message}" : "";
//                TempData["Error"] = $"WYJĄTEK KRYTYCZNY: {ex.Message}{innerMsg}. {debugInfo}";
//            }

//            return RedirectToAction(nameof(Index));
//        }

//        [HttpGet]
//        [AllowAnonymous]
//        public async Task<IActionResult> TestConnectivity()
//        {
//            var sb = new System.Text.StringBuilder();
//            sb.AppendLine($"--- DIAGNOSTYKA OSTATECZNA [v3.0 - SocketsHttpHandler] ---");
//            sb.AppendLine($"Czas: {DateTime.Now}");
//            sb.AppendLine($"System OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
//            sb.AppendLine($"Architektura OS: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
//            sb.AppendLine($"Framework: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
//            sb.AppendLine("------------------------------------------------");

//            string host = "sandbox.api.imoje.pl";
//            int port = 443;
//            string testUrl = $"https://{host}/v1/merchant/settings/ips";

//            try
//            {

//                sb.AppendLine($"\n[1. DNS LOOKUP]");
//                var ipEntry = await System.Net.Dns.GetHostEntryAsync(host);
//                foreach (var ip in ipEntry.AddressList)
//                {
//                    sb.AppendLine($"   -> IP: {ip} ({ip.AddressFamily})");
//                }

//                var targetIp = ipEntry.AddressList.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
//                if (targetIp == null)
//                {
//                    sb.AppendLine("   BŁĄD: Brak IPv4.");
//                    return Content(sb.ToString(), "text/plain");
//                }

//                var protocolsToTest = new[]
//                {
//                    System.Security.Authentication.SslProtocols.Tls12,
//                    System.Security.Authentication.SslProtocols.Tls13
//                };

//                foreach (var protocol in protocolsToTest)
//                {
//                    sb.AppendLine($"\n[2. TEST SYSTEMOWY: {protocol}]");
//                    try
//                    {
//                        using (var tcpClient = new System.Net.Sockets.TcpClient())
//                        {
//                            await tcpClient.ConnectAsync(targetIp, port);
//                            using (var sslStream = new System.Net.Security.SslStream(tcpClient.GetStream(), false, (s, c, ch, e) => true))
//                            {
//                                sb.Append($"   Próba handshake {protocol}... ");
//                                await sslStream.AuthenticateAsClientAsync(host, null, protocol, false);
//                                sb.AppendLine("SUKCES!");
//                                sb.AppendLine($"   Szyfr: {sslStream.CipherAlgorithm} | Protokół: {sslStream.SslProtocol}");
//                            }
//                        }
//                    }
//                    catch (Exception ex)
//                    {
//                        sb.AppendLine($"   BŁĄD: {ex.Message}");
//                        if (ex.InnerException != null) sb.AppendLine($"   Inner: {ex.InnerException.Message}");
//                    }
//                }

//                sb.AppendLine($"\n[3. TEST .NET 9 HTTP CLIENT (Wymuszenie TLS 1.3)]");
//                sb.AppendLine("   Testujemy czy SocketsHttpHandler potrafi wymusić TLS 1.3 na tym systemie...");

//                try
//                {
//                    var handler = new SocketsHttpHandler();
//                    handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
//                    {

//                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls13,
//                        RemoteCertificateValidationCallback = (s, c, ch, e) => true
//                    };
//                    handler.ConnectTimeout = TimeSpan.FromSeconds(5);

//                    using (var client = new HttpClient(handler))
//                    {
//                        client.Timeout = TimeSpan.FromSeconds(5);
//                        client.DefaultRequestHeaders.Add("User-Agent", "TestAgent");

//                        sb.Append("   Wysyłam GET... ");
//                        var response = await client.GetAsync(testUrl);
//                        sb.AppendLine($"SUKCES! Status: {response.StatusCode}");
//                    }
//                }
//                catch (PlatformNotSupportedException platEx)
//                {
//                    sb.AppendLine("\n   !!! DOWÓD DLA WEBIO !!!");
//                    sb.AppendLine($"   BŁĄD: PlatformNotSupportedException");
//                    sb.AppendLine($"   Treść: {platEx.Message}");
//                    sb.AppendLine("   WNIOSEK: Ten system operacyjny NIE WSPIERA TLS 1.3 w .NET 9.");
//                }
//                catch (Exception ex)
//                {
//                    sb.AppendLine($"   BŁĄD: {ex.GetType().Name}");
//                    sb.AppendLine($"   Treść: {ex.Message}");
//                    if (ex.InnerException != null) sb.AppendLine($"   Inner: {ex.InnerException.Message}");
//                }

//                sb.AppendLine($"\n[4. TEST .NET 9 (Mieszany Tls12 | Tls13)]");
//                try
//                {
//                    var handler = new SocketsHttpHandler();
//                    handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
//                    {
//                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
//                        RemoteCertificateValidationCallback = (s, c, ch, e) => true
//                    };

//                    using (var client = new HttpClient(handler))
//                    {
//                        client.DefaultRequestHeaders.Add("User-Agent", "TestAgent");
//                        sb.Append("   Wysyłam GET... ");
//                        var response = await client.GetAsync(testUrl);
//                        sb.AppendLine($"SUKCES! Status: {response.StatusCode}");
//                    }
//                }
//                catch (Exception ex)
//                {
//                    sb.AppendLine($"   BŁĄD: {ex.Message}");
//                    if (ex.Message.Contains("EOF"))
//                    {
//                        sb.AppendLine("   INTERPRETACJA: Serwer (My) wysłał TLS 1.2, Bank odrzucił połączenie (EOF).");
//                    }
//                }

//            }
//            catch (Exception ex)
//            {
//                sb.AppendLine($"\n!!! KRYTYCZNY BŁĄD SKRYPTU !!!\n{ex}");
//            }

//            return Content(sb.ToString(), "text/plain");
//        }

//    }
//}



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
using PriceSafari.Services.KSeF;
using Microsoft.Extensions.Configuration;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class InvoiceController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly IImojeService _imojeService;
        private readonly IConfiguration _configuration;
        private readonly IKSeFClientWrapper _ksefClient;
        private readonly IInvoiceXmlBuilder _xmlBuilder;

        public InvoiceController(
            PriceSafariContext context,
            IImojeService imojeService,
            IConfiguration configuration,
            IKSeFClientWrapper ksefClient,
            IInvoiceXmlBuilder xmlBuilder)
        {
            _context = context;
            _imojeService = imojeService;
            _configuration = configuration;
            _ksefClient = ksefClient;
            _xmlBuilder = xmlBuilder;
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

        // ============================================================
        // NOWA AKCJA: Ręczna wysyłka faktury do KSeF
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendToKSeF(int id)
        {
            var invoice = await _context.Invoices
                .Include(i => i.Store)
                    .ThenInclude(s => s.PaymentData)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.InvoiceId == id);

            if (invoice == null)
            {
                TempData["Error"] = "Nie znaleziono faktury.";
                return RedirectToAction(nameof(Index));
            }

            if (invoice.IsExportedToKSeF)
            {
                TempData["Warning"] = $"FV {invoice.InvoiceNumber} już w KSeF (nr: {invoice.KSeFInvoiceNumber}).";
                return RedirectToAction(nameof(Index));
            }

            if (invoice.KSeFStatus == KSeFExportStatus.Pending)
            {
                TempData["Warning"] = $"FV {invoice.InvoiceNumber} w trakcie przetwarzania (Ref: {invoice.KSeFReferenceNumber}).";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrEmpty(invoice.NIP))
            {
                TempData["Error"] = $"FV {invoice.InvoiceNumber} — brak NIP nabywcy (wymagany przez KSeF).";
                return RedirectToAction(nameof(Index));
            }

            var env = Environment.GetEnvironmentVariable("KSEF_ENVIRONMENT") ?? "?";

            try
            {
                var authenticated = await _ksefClient.AuthenticateAsync(CancellationToken.None);
                if (!authenticated)
                {
                    TempData["Error"] = $"Błąd auth KSeF ({env}). Sprawdź KSEF_TOKEN i KSEF_BASE_URL w .env.";
                    return RedirectToAction(nameof(Index));
                }

                var xml = _xmlBuilder.BuildInvoiceXml(invoice);

                var (success, refNumber, error) =
                    await _ksefClient.SendInvoiceAsync(xml, CancellationToken.None);

                if (success)
                {
                    invoice.KSeFStatus = KSeFExportStatus.Pending;
                    invoice.KSeFReferenceNumber = refNumber;
                    invoice.KSeFExportDate = DateTime.Now;
                    invoice.KSeFErrorMessage = null;
                    await _context.SaveChangesAsync();

                    TempData["Success"] = $"Wysłano FV {invoice.InvoiceNumber} do KSeF ({env})! Ref: {refNumber}";

                    // Próba natychmiastowego sprawdzenia statusu
                    await Task.Delay(3000);
                    await TryConfirmKSeFStatus(invoice);
                }
                else
                {
                    invoice.KSeFStatus = KSeFExportStatus.Error;
                    invoice.KSeFErrorMessage = error?.Length > 500 ? error[..500] : error;
                    await _context.SaveChangesAsync();

                    TempData["Error"] = $"Błąd KSeF ({env}): {error}";
                }
            }
            catch (Exception ex)
            {
                invoice.KSeFStatus = KSeFExportStatus.Error;
                invoice.KSeFErrorMessage = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                await _context.SaveChangesAsync();

                TempData["Error"] = $"Wyjątek KSeF: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // NOWA AKCJA: Sprawdź status KSeF (dla faktur Pending)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckKSeFStatus(int id)
        {
            var invoice = await _context.Invoices
                .FirstOrDefaultAsync(i => i.InvoiceId == id);

            if (invoice == null)
            {
                TempData["Error"] = "Nie znaleziono faktury.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrEmpty(invoice.KSeFReferenceNumber))
            {
                TempData["Warning"] = "Brak ReferenceNumber — faktura nie była wysyłana do KSeF.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var authenticated = await _ksefClient.AuthenticateAsync(CancellationToken.None);
                if (!authenticated)
                {
                    TempData["Error"] = "Nie udało się uwierzytelnić w KSeF.";
                    return RedirectToAction(nameof(Index));
                }

                await TryConfirmKSeFStatus(invoice);

                if (invoice.KSeFStatus == KSeFExportStatus.Confirmed)
                    TempData["Success"] = $"KSeF potwierdził FV {invoice.InvoiceNumber}! Nr: {invoice.KSeFInvoiceNumber}";
                else if (invoice.KSeFStatus == KSeFExportStatus.Error)
                    TempData["Error"] = $"KSeF error: {invoice.KSeFErrorMessage}";
                else
                    TempData["Warning"] = "Wciąż przetwarzane (Pending). Spróbuj za chwilę.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Wyjątek: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task TryConfirmKSeFStatus(InvoiceClass invoice)
        {
            if (string.IsNullOrEmpty(invoice.KSeFReferenceNumber)) return;

            var (status, ksefNumber, error) =
                await _ksefClient.CheckStatusAsync(invoice.KSeFReferenceNumber, CancellationToken.None);

            if (status == KSeFExportStatus.Confirmed && !string.IsNullOrEmpty(ksefNumber))
            {
                invoice.IsExportedToKSeF = true;
                invoice.KSeFInvoiceNumber = ksefNumber;
                invoice.KSeFStatus = KSeFExportStatus.Confirmed;
                invoice.KSeFErrorMessage = null;
            }
            else if (status == KSeFExportStatus.Error)
            {
                invoice.KSeFStatus = KSeFExportStatus.Error;
                invoice.KSeFErrorMessage = error?.Length > 500 ? error[..500] : error;
            }

            await _context.SaveChangesAsync();
        }

        // ============================================================
        // ISTNIEJĄCE AKCJE (bez zmian)
        // ============================================================

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
                if (invoice.DueDate == null) invoice.DueDate = invoice.IssueDate.AddDays(14);
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
            var invoice = await _context.Invoices.Include(i => i.Store).Include(i => i.Plan).FirstOrDefaultAsync(i => i.InvoiceId == id);
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
                        if (store != null) { store.ProductsToScrap = store.Plan.ProductsToScrap; await _context.SaveChangesAsync(); }
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
            var invoice = await _context.Invoices.Include(i => i.Store).Include(i => i.Plan).FirstOrDefaultAsync(i => i.InvoiceId == id);
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
                if (store != null) { store.PlanId = invoice.PlanId; store.ProductsToScrap = invoice.Plan.ProductsToScrap; }
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

        private bool InvoiceExists(int id) => _context.Invoices.Any(e => e.InvoiceId == id);

        [HttpGet]
        public async Task<IActionResult> DownloadPdf(int id)
        {
            var invoice = await _context.Invoices.Include(i => i.Store).Include(i => i.Plan).FirstOrDefaultAsync(i => i.InvoiceId == id);
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
            var invoice = await _context.Invoices.Include(i => i.Store).FirstOrDefaultAsync(i => i.InvoiceId == id);
            if (invoice == null) { TempData["Error"] = "Nie znaleziono faktury."; return RedirectToAction(nameof(Index)); }
            if (invoice.IsPaid) { TempData["Warning"] = "Ta faktura jest już opłacona."; return RedirectToAction(nameof(Index)); }
            if (invoice.Store == null || !invoice.Store.IsRecurringActive || string.IsNullOrEmpty(invoice.Store.ImojePaymentProfileId))
            { TempData["Error"] = "Sklep nie posiada aktywnej karty płatniczej."; return RedirectToAction(nameof(Index)); }

            string configIp = _configuration["SERVER_PUBLIC_IP"];
            string usedIp = string.IsNullOrEmpty(configIp) ? "127.0.0.1" : configIp;
            string debugInfo = $"[IP: '{usedIp}'] [Profile: {invoice.Store.ImojePaymentProfileId}]";

            try
            {
                var (success, responseMessage) = await _imojeService.ChargeProfileAsync(invoice.Store.ImojePaymentProfileId, invoice, usedIp);
                if (success)
                {
                    invoice.IsPaid = true; invoice.PaymentDate = DateTime.Now; invoice.IsPaidByCard = true;
                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"SUKCES: Pobrano środki. {debugInfo}";
                }
                else { TempData["Error"] = $"BŁĄD PŁATNOŚCI! Imoje: {responseMessage} {debugInfo}"; }
            }
            catch (Exception ex)
            {
                string innerMsg = ex.InnerException != null ? $" | Inner: {ex.InnerException.Message}" : "";
                TempData["Error"] = $"WYJĄTEK: {ex.Message}{innerMsg}. {debugInfo}";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}