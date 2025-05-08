using System;
using System.Collections.Generic; // Dodaj ten using dla List<string>
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace PriceSafari.Services.ControlNetwork
{
    [ApiController]
    [Route("api/[controller]")]
    public class ControlNetworkController : ControllerBase
    {
        private readonly ILogger<ControlNetworkController> _logger;
        public static event EventHandler NetworkDisabled;

        public ControlNetworkController(ILogger<ControlNetworkController> logger)
            => _logger = logger;

        private async Task<IActionResult> ExecuteDisableAsync()
        {
            // Lista interfejsów VPN Avasta, które chcemy spróbować wyłączyć
            var targetInterfaceNames = new List<string>
            {
                "Avast SecureLine VPN",
                "Avast SecureLine VPN WireGuard"
            };

            _logger.LogInformation(">>> Rozpoczęto próbę wyłączenia interfejsów VPN Avasta.");
            bool anySuccess = false;
            List<string> errors = new List<string>();

            foreach (var interfaceName in targetInterfaceNames)
            {
                _logger.LogInformation($"Próba wyłączenia interfejsu: {interfaceName}");
                var args = $"interface set interface name=\"{interfaceName}\" admin=disabled";

                var psi = new ProcessStartInfo("netsh", args)
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                try
                {
                    _logger.LogInformation($"Uruchamianie netsh dla '{interfaceName}' z argumentami: {args} i Verb=runas");
                    using (var proc = Process.Start(psi))
                    {
                        if (proc == null)
                        {
                            string errorMsg = $"Nie udało się wystartować procesu netsh dla interfejsu '{interfaceName}'.";
                            _logger.LogError(errorMsg);
                            errors.Add(errorMsg);
                            continue; // Przejdź do następnego interfejsu
                        }

                        proc.WaitForExit(); // Poczekaj na zakończenie procesu

                        _logger.LogInformation($"Proces netsh dla '{interfaceName}' zakończony. Kod wyjścia: {proc.ExitCode}");

                        if (proc.ExitCode == 0)
                        {
                            _logger.LogInformation($"Interfejs '{interfaceName}' został pomyślnie wyłączony (lub był już wyłączony).");
                            anySuccess = true;
                        }
                        else
                        {
                            // netsh może zwrócić błąd, jeśli interfejs nie istnieje.
                            // Można by to specyficznie obsłużyć, jeśli znamy kod błędu dla "nie znaleziono interfejsu".
                            // Na razie traktujemy każdy niezerowy kod jako potencjalny problem.
                            string errorMsg = $"Polecenie netsh dla interfejsu '{interfaceName}' nie powiodło się. Kod błędu: {proc.ExitCode}.";
                            _logger.LogWarning(errorMsg); // Logujemy jako ostrzeżenie, bo np. brak interfejsu nie jest krytyczny
                            errors.Add(errorMsg);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Ten wyjątek może być np. Win32Exception jeśli użytkownik anuluje monit UAC.
                    string errorMsg = $"Wystąpił wyjątek podczas próby wyłączenia interfejsu '{interfaceName}': {ex.Message}";
                    _logger.LogError(ex, errorMsg);
                    errors.Add(errorMsg);
                }
            }

            if (!anySuccess && errors.Count == targetInterfaceNames.Count) // Jeśli żadna operacja się nie powiodła i dla każdej był błąd
            {
                _logger.LogError("Nie udało się pomyślnie wykonać operacji netsh dla żadnego z docelowych interfejsów.");
                return StatusCode(500, "Nie udało się wyłączyć żadnego z docelowych interfejsów. Szczegóły w logach: " + string.Join("; ", errors));
            }

            // Jeśli przynajmniej jedna operacja się udała lub nie było krytycznych błędów uniemożliwiających start procesu
            _logger.LogInformation("Zakończono próby wyłączania interfejsów. Oczekiwanie 30 sekund...");
            await Task.Delay(TimeSpan.FromSeconds(30));
            _logger.LogInformation("Zakończono oczekiwanie. Wywoływanie eventu NetworkDisabled.");
            NetworkDisabled?.Invoke(this, EventArgs.Empty);

            // Zwracamy sukces, nawet jeśli niektóre interfejsy nie zostały znalezione, ale przynajmniej jedna operacja 'disable' mogła się powieść.
            // Możesz dostosować logikę zwracanego komunikatu w oparciu o `anySuccess` i zawartość `errors`.
            string finalMessage = anySuccess ? "Przynajmniej jeden interfejs VPN został wyłączony. Odczekano i zgłoszono." : "Nie udało się potwierdzić wyłączenia żadnego interfejsu, ale proces został wykonany. Odczekano i zgłoszono.";
            if (errors.Count > 0 && anySuccess)
            {
                finalMessage += " Wystąpiły problemy z niektórymi interfejsami: " + string.Join("; ", errors);
            }

            return Ok(new { success = true, message = finalMessage, details = errors });
        }

        [HttpPost("disable")]
        public Task<IActionResult> DisableNetworkPost()
            => ExecuteDisableAsync();

        [HttpGet("disable")] // Dla testów z przeglądarki
        public Task<IActionResult> DisableNetworkGet()
            => ExecuteDisableAsync();
    }
}