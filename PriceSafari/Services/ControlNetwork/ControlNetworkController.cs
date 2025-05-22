using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging; 


namespace PriceSafari.Services.ControlNetwork 
{
    public interface INetworkControlService 
    {
        Task<bool> TriggerNetworkDisableAndResetAsync();
        event EventHandler NetworkResetCompleted; 
    }

    public class NetworkControlService : INetworkControlService
    {
        private readonly ILogger<NetworkControlService> _logger;
        public event EventHandler NetworkResetCompleted; 

        public NetworkControlService(ILogger<NetworkControlService> logger)
        {
            _logger = logger;
        }

        // Główna publiczna metoda
        public async Task<bool> TriggerNetworkDisableAndResetAsync()
        {
            _logger.LogInformation(">>> TriggerNetworkDisableAndResetAsync called.");
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
                            continue;
                        }
                        proc.WaitForExit();
                        _logger.LogInformation($"Proces netsh dla '{interfaceName}' zakończony. Kod wyjścia: {proc.ExitCode}");
                        if (proc.ExitCode == 0)
                        {
                            _logger.LogInformation($"Interfejs '{interfaceName}' został pomyślnie wyłączony.");
                            anySuccess = true;
                        }
                        else
                        {
                            string errorMsg = $"Polecenie netsh dla '{interfaceName}' nie powiodło się. Kod błędu: {proc.ExitCode}.";
                            _logger.LogWarning(errorMsg);
                            errors.Add(errorMsg);
                        }
                    }
                }
                catch (Exception ex)
                {
                    string errorMsg = $"Wystąpił wyjątek podczas próby wyłączenia interfejsu '{interfaceName}': {ex.Message}";
                    _logger.LogError(ex, errorMsg);
                    errors.Add(errorMsg);
                }
            }

            if (!anySuccess && errors.Count == targetInterfaceNames.Count)
            {
                _logger.LogError("Nie udało się pomyślnie wykonać operacji netsh dla żadnego z docelowych interfejsów.");
                return false; // Zwróć false jeśli całkowita porażka
            }

            _logger.LogInformation("Zakończono próby wyłączania interfejsów. Oczekiwanie 15 sekund...");
            await Task.Delay(TimeSpan.FromSeconds(15)); 
            _logger.LogInformation("Zakończono oczekiwanie. Wywoływanie eventu NetworkResetCompleted.");
            NetworkResetCompleted?.Invoke(this, EventArgs.Empty); 

            return true; 
        }
    }
}
