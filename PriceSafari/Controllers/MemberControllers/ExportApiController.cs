// Nie używamy tu [Authorize] opartego na ciastkach, bo to API dla maszyn
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;

[ApiController]
[Route("DataTree/export")]
public class ExportApiController : ControllerBase
{
    private readonly PriceSafariContext _context;

    public ExportApiController(PriceSafariContext context)
    {
        _context = context;
    }

    // GET: /api/export/{storeId}?token=TWOJ_TOKEN&format=json
    [HttpGet("{storeId}")]
    public async Task<IActionResult> GetFeed(int storeId, [FromQuery] string token, [FromQuery] string format = "json")
    {
        // 1. Podstawowa walidacja wejścia
        if (string.IsNullOrWhiteSpace(token))
        {
            return Unauthorized(new { error = "Brak tokenu dostępu." });
        }

        // 2. Weryfikacja uprawnień i tokenu w bazie danych (AsNoTracking dla szybkości)
        var store = await _context.Stores
            .AsNoTracking()
            .Select(s => new { s.StoreId, s.IsApiExportEnabled, s.ApiExportToken })
            .FirstOrDefaultAsync(s => s.StoreId == storeId);

        if (store == null)
        {
            return NotFound(new { error = "Sklep nie istnieje." });
        }

        if (!store.IsApiExportEnabled)
        {
            return StatusCode(403, new { error = "Eksport API dla tego sklepu jest wyłączony." });
        }

        if (store.ApiExportToken != token)
        {
            return Unauthorized(new { error = "Nieprawidłowy token dostępu." });
        }

        // 3. Ustalenie żądanego formatu i ścieżki do pliku
        var extension = format.ToLower() == "xml" ? "xml" : "json";
        var exportFolder = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "PriceExports");
        var filePath = Path.Combine(exportFolder, $"feed_{storeId}.{extension}");

        // 4. Sprawdzenie, czy plik zdążył się już wygenerować
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(new { error = "Plik z danymi jeszcze nie istnieje. Poczekaj na zakończenie pierwszego skanowania." });
        }

        // 5. Zwrócenie pliku - strumieniowanie bezpośrednio z dysku (ZERO obciążenia RAM!)
        var contentType = extension == "xml" ? "application/xml" : "application/json";

        return PhysicalFile(filePath, contentType);
    }
}