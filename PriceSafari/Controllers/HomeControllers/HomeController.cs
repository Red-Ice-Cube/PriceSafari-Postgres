using Microsoft.AspNetCore.Mvc;
using PriceSafari.Data;
using PriceSafari.Models.ViewModels;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Models.HomeModels;
using PriceSafari.Services.ViewRenderService;

namespace PriceSafari.Controllers.HomeControllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly PriceSafariContext _context;
        private readonly IEmailSender _emailSender;
        private readonly IViewRenderService _viewRenderService;

        public HomeController(PriceSafariContext context, ILogger<HomeController> logger, IEmailSender emailSender, IViewRenderService viewRenderService)
        {
            _logger = logger;
            _context = context;
            _emailSender = emailSender;
            _viewRenderService = viewRenderService;
        }


        public async Task<IActionResult> Index()
        {
            var faqItems = new List<FAQItem>
            {
                new FAQItem
                {
                    Question = "Co jest potrzebne, aby zacząć?",
                    Answer = @"<p>Aby rozpocząć monitoring cen na platformie Ceneo, potrzebujemy jedynie adresu URL Twojego sklepu internetowego. Nie musisz dostarczać nam feedu produktowego – znajdziemy wszystkie produkty oferowane na Twojej witrynie, a następnie, w zależności od wybranego pakietu, sam zdecydujesz, które ceny chcesz monitorować.</p>
                    <p>Informacje o naszych podstawowych pakietach znajdziesz <a href=""URL_TUTAJ"">tutaj</a>.</p>
                    <p>Jeśli chcesz skorzystać z dodatkowych funkcji, takich jak dodanie zdjęć produktów, integracja z Twoim sklepem przez API czy możliwość aktualizacji cen jednym kliknięciem z poziomu panelu, będziemy potrzebowali Twojego feedu produktowego z platformy Ceneo. Umożliwi nam to połączenie Twoich ofert z Twoim sklepem, dzięki czemu, na przykład, gdy zaktualizujesz ceny, będziesz mógł natychmiast zobaczyć, które produkty zostały zmienione.</p>
                    <p>Do monitorowania cen w Europie potrzebujemy od Ciebie feedu produktowego z Google Merchant Center. Po jego otrzymaniu proces jest automatyczny – wyszukamy Twoje produkty, a Ty wybierzesz, które z nich oraz w jakich krajach chcesz śledzić.</p>
                    <p>Więcej o raportach Safari dowiesz się <a href=""URL_TUTAJ"">tutaj</a>.</p>"
                },
                new FAQItem
                {
                    Question = "Skąd pochodzą dane o cenach?",
                    Answer = @"<p>Nasze analizy opierają się na danych pozyskiwanych z Google Shopping oraz Ceneo. Ponieważ Allegro eksportuje swoje oferty do porównywarek cenowych i Google, jesteśmy w stanie dostarczyć także dane pochodzące z Allegro.</p>
                    <p>Najbardziej wartościowe informacje pozyskujemy z platformy Ceneo. Oprócz ceny i kosztów dostawy zyskasz także dostęp do szczegółowych danych, takich jak pozycja oferty w rankingu, informacja o promocji oferty, dostępność produktu w magazynie oraz przewidywany czas dostawy deklarowany przez sklep.</p>"
                },
                new FAQItem
                {
                    Question = "Ile kosztuje monitoring cen?",
                    Answer = @"<p>Koszt monitoringu cen zależy od kilku czynników, takich jak zakres usług oraz częstotliwość aktualizacji danych. Nasze podstawowe pakiety obejmują monitoring danych z platformy Ceneo, z aktualizacją co 24 godziny. Szczegółowe informacje o cenach znajdziesz <a href=""URL_TUTAJ"">tutaj</a>.</p>"
                },
                new FAQItem
                {
                    Question = "Czy obsługa dodatkowego programu zajmie mi więcej czasu?",
                    Answer = @"<p>Dzięki szybkiemu wdrożeniu oraz intuicyjnemu panelowi obsługi, nasz program nie wymaga zatrudniania dodatkowego pracownika.
                    W przeciwieństwie do konkurencyjnych rozwiązań, zaprojektowaliśmy platformę tak, aby dostarczać kluczowe dane dla działu handlowego w Twojej firmie.
                    Naszym celem jest, by dostarczane informacje były zrozumiałe i czytelne dla każdego, eliminując potrzebę angażowania dodatkowych zasobów do monitorowania cen.</p>
                    <p>Zautomatyzowane procesy oszczędzą czas poświęcany na ręczne przeglądanie cen – na przykład przed weekendem czy przy wprowadzaniu nowej gamy produktów.
                    Dzięki dostępowi do bieżących informacji o działaniach konkurencji, Twój zespół handlowy może skupić się na tworzeniu lepiej zoptymalizowanej oferty, co dodatkowo wspiera wzrost efektywności.</p>
                    <p>Nasza platforma dostarcza również szczegółowe dane dotyczące efektywności budżetu reklamowego.
                    Możesz śledzić, które produkty przynoszą realne zyski, a które nie generują sprzedaży, co pozwala na bardziej świadome decyzje marketingowe – na przykład rezygnację z promowania nieopłacalnych produktów.
                    Wgrywając marże, masz również pełną kontrolę nad zyskownością: sprawdzisz, czy przez niedopatrzenie nie sprzedajesz produktów poniżej planowanej rentowności lub, czy będąc liderem cenowym, nie warto podnieść ceny w celu maksymalizacji zysków.</p>
                    <p>Tego rodzaju optymalizacje mogą przynieść oszczędności sięgające kilkudziesięciu tysięcy złotych miesięcznie, jednocześnie zwiększając Twoją sprzedaż.</p>"
                }
            };

            ViewBag.FAQItems = faqItems;

            return View();
        }


        public async Task<IActionResult> FAQ()
        {
            var faqItems = new List<FAQItem>
            {
                new FAQItem
                {
                    Question = "Co jest potrzebne, aby zacząć?",
                    Answer = @"<p>Aby rozpocząć monitoring cen na platformie Ceneo, potrzebujemy jedynie adresu URL Twojego sklepu internetowego. Nie musisz dostarczać nam feedu produktowego – znajdziemy wszystkie produkty oferowane na Twojej witrynie, a następnie, w zależności od wybranego pakietu, sam zdecydujesz, które ceny chcesz monitorować.</p>
                    <p>Informacje o naszych podstawowych pakietach znajdziesz <a href=""URL_TUTAJ"">tutaj</a>.</p>
                    <p>Jeśli chcesz skorzystać z dodatkowych funkcji, takich jak dodanie zdjęć produktów, integracja z Twoim sklepem przez API czy możliwość aktualizacji cen jednym kliknięciem z poziomu panelu, będziemy potrzebowali Twojego feedu produktowego z platformy Ceneo. Umożliwi nam to połączenie Twoich ofert z Twoim sklepem, dzięki czemu, na przykład, gdy zaktualizujesz ceny, będziesz mógł natychmiast zobaczyć, które produkty zostały zmienione.</p>
                    <p>Do monitorowania cen w Europie potrzebujemy od Ciebie feedu produktowego z Google Merchant Center. Po jego otrzymaniu proces jest automatyczny – wyszukamy Twoje produkty, a Ty wybierzesz, które z nich oraz w jakich krajach chcesz śledzić.</p>
                    <p>Więcej o raportach Safari dowiesz się <a href=""URL_TUTAJ"">tutaj</a>.</p>"
                },
                new FAQItem
                {
                    Question = "Skąd pochodzą dane o cenach?",
                    Answer = @"<p>Nasze analizy opierają się na danych pozyskiwanych z Google Shopping oraz Ceneo. Ponieważ Allegro eksportuje swoje oferty do porównywarek cenowych i Google, jesteśmy w stanie dostarczyć także dane pochodzące z Allegro.</p>
                    <p>Najbardziej wartościowe informacje pozyskujemy z platformy Ceneo. Oprócz ceny i kosztów dostawy zyskasz także dostęp do szczegółowych danych, takich jak pozycja oferty w rankingu, informacja o promocji oferty, dostępność produktu w magazynie oraz przewidywany czas dostawy deklarowany przez sklep.</p>"
                },
                new FAQItem
                {
                    Question = "Ile kosztuje monitoring cen?",
                    Answer = @"<p>Koszt monitoringu cen zależy od kilku czynników, takich jak zakres usług oraz częstotliwość aktualizacji danych. Nasze podstawowe pakiety obejmują monitoring danych z platformy Ceneo, z aktualizacją co 24 godziny. Szczegółowe informacje o cenach znajdziesz <a href=""URL_TUTAJ"">tutaj</a>.</p>"
                },
                new FAQItem
                {
                    Question = "Czy obsługa dodatkowego programu zajmie mi więcej czasu?",
                    Answer = @"<p>Dzięki szybkiemu wdrożeniu oraz intuicyjnemu panelowi obsługi, nasz program nie wymaga zatrudniania dodatkowego pracownika.
                    W przeciwieństwie do konkurencyjnych rozwiązań, zaprojektowaliśmy platformę tak, aby dostarczać kluczowe dane dla działu handlowego w Twojej firmie.
                    Naszym celem jest, by dostarczane informacje były zrozumiałe i czytelne dla każdego, eliminując potrzebę angażowania dodatkowych zasobów do monitorowania cen.</p>
                    <p>Zautomatyzowane procesy oszczędzą czas poświęcany na ręczne przeglądanie cen – na przykład przed weekendem czy przy wprowadzaniu nowej gamy produktów.
                    Dzięki dostępowi do bieżących informacji o działaniach konkurencji, Twój zespół handlowy może skupić się na tworzeniu lepiej zoptymalizowanej oferty, co dodatkowo wspiera wzrost efektywności.</p>
                    <p>Nasza platforma dostarcza również szczegółowe dane dotyczące efektywności budżetu reklamowego.
                    Możesz śledzić, które produkty przynoszą realne zyski, a które nie generują sprzedaży, co pozwala na bardziej świadome decyzje marketingowe – na przykład rezygnację z promowania nieopłacalnych produktów.
                    Wgrywając marże, masz również pełną kontrolę nad zyskownością: sprawdzisz, czy przez niedopatrzenie nie sprzedajesz produktów poniżej planowanej rentowności lub, czy będąc liderem cenowym, nie warto podnieść ceny w celu maksymalizacji zysków.</p>
                    <p>Tego rodzaju optymalizacje mogą przynieść oszczędności sięgające kilkudziesięciu tysięcy złotych miesięcznie, jednocześnie zwiększając Twoją sprzedaż.</p>"
                }
            };

            ViewBag.FAQItems = faqItems;

            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Contact(ContactFormViewModel model)
        {
            if (ModelState.IsValid)
            {
                var submission = new ContactFormSubmission
                {
                    Email = model.Email,
                    CompanyName = model.CompanyName,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    ConsentToDataProcessing = model.ConsentToDataProcessing,
                    PhoneNumber = model.PhoneNumber,
                    PrefersPhone = model.PrefersPhone,
                    SubmissionDate = DateTime.Now
                };

                _context.ContactFormSubmissions.Add(submission);
                await _context.SaveChangesAsync();

                // Wysłanie powiadomienia email do administratora
                var adminSubject = "Nowe zgłoszenie z formularza kontaktowego";
                var adminMessage = $"Nowe zgłoszenie od {submission.FirstName} {submission.LastName}";
                await _emailSender.SendEmailAsync("twoj_email@przyklad.pl", adminSubject, adminMessage);

                // Wysłanie emaila z podziękowaniem do użytkownika
                var userSubject = "Dziękujemy za kontakt z nami!";
                string userMessage = await _viewRenderService.RenderToStringAsync("EmailTemplates/ThankYouEmail", submission);
                await _emailSender.SendEmailAsync(submission.Email, userSubject, userMessage);

                // Ustawienie flagi informującej o sukcesie
                ViewBag.FormSubmitted = true;

                // Opcjonalnie: Możesz wyczyścić model, aby nie wyświetlał danych w polach formularza
                ModelState.Clear();
                return View();
            }
            return View(model);
        }





    }
}
