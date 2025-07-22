using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PriceSafari.Services.ControlNetwork
{
    public interface INetworkControlService
    {
        Task<bool> TriggerNetworkDisableAndResetAsync(CancellationToken cancellationToken = default);
        event EventHandler NetworkResetCompleted;
    }

    public class NetworkControlService : INetworkControlService
    {
        private readonly ILogger<NetworkControlService> _logger;
        public event EventHandler NetworkResetCompleted;

        private static readonly List<string> TargetVpnInterfaceNames = new List<string>
        {
            "Avast SecureLine VPN",
            "Avast SecureLine VPN WireGuard"

        };

        public NetworkControlService(ILogger<NetworkControlService> logger)
        {
            _logger = logger;
        }

        private async Task<bool> IsInterfaceConnectedAsync(string interfaceName, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo("netsh", $"interface show interface name=\"{interfaceName}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using (var proc = new Process { StartInfo = psi })
                {
                    _logger.LogTrace($"Sprawdzanie statusu interfejsu: '{interfaceName}' poleceniem: {psi.FileName} {psi.Arguments}");
                    proc.Start();

                    var outputTask = proc.StandardOutput.ReadToEndAsync();
                    var errorTask = proc.StandardError.ReadToEndAsync();

                    bool processExited = await Task.Run(() => proc.WaitForExit(5000), cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation($"Sprawdzanie statusu interfejsu '{interfaceName}' anulowane.");
                        if (!proc.HasExited) try { proc.Kill(); } catch { }
                        return false;
                    }

                    if (!processExited)
                    {
                        _logger.LogWarning($"Proces netsh dla interfejsu '{interfaceName}' przekroczył limit czasu (5s). Zabijam proces.");
                        try { proc.Kill(); } catch { }
                        return false;
                    }

                    string output = await outputTask;
                    string errorOutput = await errorTask;

                    if (proc.ExitCode != 0)
                    {
                        _logger.LogWarning($"Polecenie netsh dla sprawdzenia statusu '{interfaceName}' zakończone kodem {proc.ExitCode}. Błąd: '{errorOutput?.Trim()}'. Wyjście: '{output?.Trim()}'");

                        if (!string.IsNullOrWhiteSpace(errorOutput) &&
                            (errorOutput.Contains("is not a valid interface name", StringComparison.OrdinalIgnoreCase) ||
                             errorOutput.Contains("The system cannot find the file specified", StringComparison.OrdinalIgnoreCase) ||
                             errorOutput.Contains("No more data is available", StringComparison.OrdinalIgnoreCase)))
                        {
                            _logger.LogInformation($"Interfejs '{interfaceName}' nie został znaleziony przez netsh.");
                        }
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(output))
                    {
                        _logger.LogWarning($"Polecenie netsh dla '{interfaceName}' nie zwróciło danych wyjściowych, ale zakończyło się kodem 0.");
                        return false;
                    }

                    bool adminEnabled = false;
                    bool connectConnected = false;

                    using (var reader = new StringReader(output))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            if (cancellationToken.IsCancellationRequested) return false;
                            string[] parts = line.Split(new[] { ':' }, 2);
                            if (parts.Length == 2)
                            {
                                string key = parts[0].Trim();
                                string value = parts[1].Trim();

                                if (key.Equals("Administrative state", StringComparison.OrdinalIgnoreCase) &&
                                    value.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                                {
                                    adminEnabled = true;
                                }
                                if (key.Equals("Connect state", StringComparison.OrdinalIgnoreCase) &&
                                    value.Equals("Connected", StringComparison.OrdinalIgnoreCase))
                                {
                                    connectConnected = true;
                                }
                            }
                        }
                    }

                    bool isActuallyConnected = adminEnabled && connectConnected;
                    if (isActuallyConnected)
                    {
                        _logger.LogInformation($"Interfejs '{interfaceName}' jest w stanie: Administracyjnym=Włączony, Połączenia=Połączony.");
                    }
                    else
                    {
                        _logger.LogTrace($"Interfejs '{interfaceName}' jest w stanie: Administracyjnym={(adminEnabled ? "Włączony" : "Wyłączony")}, Połączenia={(connectConnected ? "Połączony" : "Rozłączony")}.");
                    }
                    return isActuallyConnected;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"Operacja sprawdzania statusu interfejsu '{interfaceName}' została anulowana.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Wyjątek podczas sprawdzania statusu interfejsu '{interfaceName}'.");
                return false;
            }
        }

        public async Task<bool> TriggerNetworkDisableAndResetAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(">>> Rozpoczęto TriggerNetworkDisableAndResetAsync.");

            _logger.LogInformation(">>> Rozpoczęto próbę wyłączenia interfejsów VPN.");
            bool anyDisableSuccess = false;
            List<string> disableErrors = new List<string>();

            foreach (var interfaceName in TargetVpnInterfaceNames)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Operacja wyłączania interfejsów przerwana (anulowanie).");
                    return false;
                }

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
                            string errorMsg = $"Nie udało się wystartować procesu netsh (do wyłączenia) dla interfejsu '{interfaceName}'.";
                            _logger.LogError(errorMsg);
                            disableErrors.Add(errorMsg);
                            continue;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

                        _logger.LogInformation($"Polecenie wyłączenia dla '{interfaceName}' zostało wysłane.");
                        anyDisableSuccess = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Operacja wyłączania interfejsu przerwana (anulowanie).");
                    return false;
                }
                catch (Exception ex)
                {

                    string errorMsg = $"Wystąpił wyjątek podczas próby wyłączenia interfejsu '{interfaceName}': {ex.Message}";
                    _logger.LogError(ex, errorMsg);
                    disableErrors.Add(errorMsg);
                }
            }

            if (!anyDisableSuccess && disableErrors.Count == TargetVpnInterfaceNames.Count)
            {
                _logger.LogError("Nie udało się zainicjować operacji wyłączenia dla żadnego z docelowych interfejsów VPN.");
                return false;
            }

            _logger.LogInformation("Zakończono próby wysyłania poleceń wyłączania interfejsów. Rozpoczynam sprawdzanie ponownego połączenia (do 60 sekund).");

            bool reconnectedWithinTimeLimit = false;
            for (int i = 0; i < 60; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Sprawdzanie ponownego połączenia przerwane (anulowanie).");
                    return false;
                }

                _logger.LogInformation($"Sprawdzanie statusu ponownego połączenia VPN... Próba {i + 1}/60");
                foreach (var interfaceName in TargetVpnInterfaceNames)
                {
                    if (await IsInterfaceConnectedAsync(interfaceName, cancellationToken))
                    {
                        _logger.LogInformation($"WYKRYTO PONOWNE POŁĄCZENIE: Interfejs '{interfaceName}' jest aktywny.");
                        reconnectedWithinTimeLimit = true;
                        break;
                    }
                }

                if (reconnectedWithinTimeLimit)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            if (reconnectedWithinTimeLimit)
            {
                _logger.LogInformation("Ponowne połączenie VPN potwierdzone. Oczekiwanie dodatkowe 10 sekundy dla stabilizacji...");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Oczekiwanie po ponownym połączeniu przerwane (anulowanie).");
                    return false;
                }

                _logger.LogInformation("Zakończono oczekiwanie. Reset sieci uznany za udany.");
                NetworkResetCompleted?.Invoke(this, EventArgs.Empty);
                return true;
            }
            else
            {
                _logger.LogWarning("Nie wykryto ponownego połączenia żadnego z interfejsów VPN w ciągu 60 sekund.");
                return false;
            }
        }
    }
}