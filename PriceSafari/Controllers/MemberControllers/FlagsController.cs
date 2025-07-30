using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PriceSafari.Data;
using PriceSafari.Models;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;


namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Member, Manager")]
    public class FlagsController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        public FlagsController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        private async Task<bool> UserHasAccessToStore(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            var isAdminOrManager = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Manager");

            if (!isAdminOrManager)
            {
                var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
                return hasAccess;
            }

            return true;
        }
        // W pliku FlagsController.cs
        public async Task<IActionResult> List(int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return Content("Nie ma takiego sklepu lub brak dostępu.");
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound("Nie znaleziono sklepu.");
            }

            // Pobierz wszystkie flagi dla sklepu
            var allFlags = await _context.Flags.Where(f => f.StoreId == storeId).ToListAsync();

            // Podziel flagi na dwie grupy
            var standardFlags = allFlags.Where(f => !f.IsMarketplace).ToList();
            var marketplaceFlags = allFlags.Where(f => f.IsMarketplace).ToList();

            // Stwórz model widoku, który przekaże wszystko
            var viewModel = new FlagsListViewModel
            {
                StandardFlags = standardFlags,
                MarketplaceFlags = marketplaceFlags,
                StoreId = storeId,
                StoreName = store.StoreName,
                HasAllegro = !string.IsNullOrEmpty(store.StoreNameAllegro)
            };

            return View("~/Views/Panel/Flags/List.cshtml", viewModel);
        }




        // W pliku FlagsController.cs

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFlagViewModel model)
        {
            // Używamy [FromBody], ponieważ dane są prawdopodobnie wysyłane jako JSON z AJAX

            if (!await UserHasAccessToStore(model.StoreId))
            {
                return Forbid();
            }

            if (ModelState.IsValid)
            {
                // Ręcznie "mapujemy" dane z prostego ViewModelu na złożony model bazy danych
                var newFlag = new FlagsClass
                {
                    FlagName = model.FlagName,
                    FlagColor = model.FlagColor,
                    StoreId = model.StoreId,
                    IsMarketplace = model.IsMarketplace
                };

                _context.Add(newFlag);
                await _context.SaveChangesAsync();

                // Zwracamy nowo utworzony obiekt - przyda się to na froncie do aktualizacji UI
                return Ok(newFlag);
            }

            // Jeśli walidacja się nie powiedzie, zwracamy błędy
            return BadRequest(ModelState);
        }


        // Możesz dodać tę klasę na dole pliku FlagsController.cs
        // lub w osobnym pliku w folderze ViewModels

        public class CreateFlagViewModel
        {
            [Required(ErrorMessage = "Nazwa flagi jest wymagana.")]
            public string FlagName { get; set; }

            [Required(ErrorMessage = "Kolor flagi jest wymagany.")]
            public string FlagColor { get; set; }

            [Required]
            public int StoreId { get; set; }

            public bool IsMarketplace { get; set; }
        }




        // Usuń metody UpdateFlagName i UpdateFlagColor i zastąp je tą jedną
        [HttpPost]
        public async Task<IActionResult> UpdateFlag(int id, string flagName, string flagColor, bool isMarketplace)
        {
            var flag = await _context.Flags.FindAsync(id);
            if (flag == null)
            {
                return NotFound();
            }

            if (!await UserHasAccessToStore(flag.StoreId))
            {
                return Forbid();
            }

            flag.FlagName = flagName;
            flag.FlagColor = flagColor;
            flag.IsMarketplace = isMarketplace; // Aktualizujemy też ten stan

            _context.Update(flag);
            await _context.SaveChangesAsync();

            return Ok();
        }

        // Model widoku, który stworzyliśmy
        public class FlagsListViewModel
        {
            public List<FlagsClass> StandardFlags { get; set; }
            public List<FlagsClass> MarketplaceFlags { get; set; }
            public int StoreId { get; set; }
            public string StoreName { get; set; }
            public bool HasAllegro { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var flag = await _context.Flags.FindAsync(id);
            if (flag == null)
            {
                return NotFound();
            }

            var productFlags = await _context.ProductFlags.Where(pf => pf.FlagId == id).ToListAsync();
            _context.ProductFlags.RemoveRange(productFlags);

            _context.Flags.Remove(flag);
            await _context.SaveChangesAsync();

            return Ok();
        }








        [HttpGet]
        public async Task<IActionResult> ExportFlags(int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return Forbid();
            }

            // 1. Pobierz definicje flag (bez zmian)
            var flags = await _context.Flags
                .Where(f => f.StoreId == storeId)
                .Select(f => new FlagDefinition
                {
                    FlagName = f.FlagName,
                    FlagColor = f.FlagColor,
                    IsMarketplace = f.IsMarketplace
                })
                .ToListAsync();

            // Stwórz mapę Nazwa -> ID (bez zmian)
            var flagIdToNameMap = await _context.Flags
                .Where(f => f.StoreId == storeId)
                .ToDictionaryAsync(f => f.FlagId, f => f.FlagName);

            // --- POPRAWIONY FRAGMENT ---

            // 2. Pobierz SUROWE powiązania z bazy danych do pamięci
            var rawAssignments = await _context.ProductFlags
                .Where(pf => pf.Flag.StoreId == storeId)
                .Select(pf => new { pf.FlagId, pf.ProductId, pf.AllegroProductId })
                .ToListAsync();

            // 3. Przetwarzanie danych W PAMIĘCI APLIKACJI z użyciem słownika
            var assignments = rawAssignments
                .Select(pf => new FlagAssignment
                {
                    // Używamy GetValueOrDefault, co jest bezpieczniejsze niż ContainsKey
                    FlagName = flagIdToNameMap.GetValueOrDefault(pf.FlagId),
                    ProductId = pf.ProductId,
                    AllegroProductId = pf.AllegroProductId
                })
                .Where(a => a.FlagName != null) // Filtrujemy w pamięci
                .ToList();

            // --- KONIEC POPRAWIONEGO FRAGMENTU ---

            // 4. Stwórz obiekt do serializacji (bez zmian)
            var exportData = new FlagExportModel
            {
                Flags = flags,
                Assignments = assignments
            };

            var store = await _context.Stores.FindAsync(storeId);
            string fileName = $"FlagsWithAssignments_{store?.StoreName?.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.json";

            return File(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(exportData, Formatting.Indented)), "application/json", fileName);
        }



        // W pliku FlagsController.cs

        [HttpPost]
        public async Task<IActionResult> ImportFlags(int storeId, IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("Nie wybrano pliku.");
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            string content;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                content = await reader.ReadToEndAsync();
            }

            try
            {
                var importData = JsonConvert.DeserializeObject<FlagExportModel>(content);
                if (importData?.Flags == null || importData.Assignments == null)
                {
                    return BadRequest("Plik jest pusty lub ma nieprawidłowy format.");
                }

                using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    // KROK 1: WYCZYŚĆ BAZĘ (bez zmian)
                    var oldFlagIds = await _context.Flags
                        .Where(f => f.StoreId == storeId)
                        .Select(f => f.FlagId)
                        .ToListAsync();

                    if (oldFlagIds.Any())
                    {
                        // Usuwamy stare powiązania
                        await _context.ProductFlags.Where(pf => oldFlagIds.Contains(pf.FlagId)).ExecuteDeleteAsync();
                        // Usuwamy stare definicje flag
                        await _context.Flags.Where(f => oldFlagIds.Contains(f.FlagId)).ExecuteDeleteAsync();
                    }

                    // KROK 2: WSTAW NOWE DEFINICJE FLAG (bez zmian)
                    foreach (var flagDef in importData.Flags)
                    {
                        var sql = "INSERT INTO Flags (FlagName, FlagColor, IsMarketplace, StoreId) VALUES (@name, @color, @isMarketplace, @storeId)";
                        await _context.Database.ExecuteSqlRawAsync(sql,
                            new Microsoft.Data.SqlClient.SqlParameter("@name", flagDef.FlagName),
                            new Microsoft.Data.SqlClient.SqlParameter("@color", flagDef.FlagColor),
                            new Microsoft.Data.SqlClient.SqlParameter("@isMarketplace", flagDef.IsMarketplace),
                            new Microsoft.Data.SqlClient.SqlParameter("@storeId", storeId)
                        );
                    }

                    // KROK 3: POBIERZ NOWE FLAGI Z ICH NOWYMI ID (bez zmian)
                    var newFlagsMap = await _context.Flags
                        .Where(f => f.StoreId == storeId)
                        .AsNoTracking()
                        .ToDictionaryAsync(f => f.FlagName, f => f.FlagId);

                    // --- KROK 4: WSTAW NOWE PRZYPISANIA (WERSJA ZAKTUALIZOWANA) ---

                    // 4a. Zbierz wszystkie ID produktów i AllegroProduktów z pliku importu
                    var productIdsFromImport = importData.Assignments
                        .Where(a => a.ProductId.HasValue)
                        .Select(a => a.ProductId.Value)
                        .Distinct().ToList();

                    var allegroProductIdsFromImport = importData.Assignments
                        .Where(a => a.AllegroProductId.HasValue)
                        .Select(a => a.AllegroProductId.Value)
                        .Distinct().ToList();

                    // 4b. Sprawdź, które z tych ID faktycznie istnieją w bazie dla danego sklepu
                    var existingProductIds = (await _context.Products
                        .Where(p => p.StoreId == storeId && productIdsFromImport.Contains(p.ProductId))
                        .Select(p => p.ProductId)
                        .ToListAsync()).ToHashSet();

                    var existingAllegroProductIds = (await _context.AllegroProducts
                        .Where(p => p.StoreId == storeId && allegroProductIdsFromImport.Contains(p.AllegroProductId))
                        .Select(p => p.AllegroProductId)
                        .ToListAsync()).ToHashSet();

                    // 4c. Iteruj przez przypisania i wstawiaj je do bazy
                    foreach (var assignment in importData.Assignments)
                    {
                        // Sprawdź, czy flaga o tej nazwie istnieje
                        if (newFlagsMap.TryGetValue(assignment.FlagName, out int flagId))
                        {
                            // Przypadek 1: Powiązanie ze standardowym produktem
                            if (assignment.ProductId.HasValue && existingProductIds.Contains(assignment.ProductId.Value))
                            {
                                var assignmentSql = "INSERT INTO ProductFlags (FlagId, ProductId, AllegroProductId) VALUES (@flagId, @productId, NULL)";
                                await _context.Database.ExecuteSqlRawAsync(assignmentSql,
                                    new Microsoft.Data.SqlClient.SqlParameter("@flagId", flagId),
                                    new Microsoft.Data.SqlClient.SqlParameter("@productId", assignment.ProductId.Value)
                                );
                            }
                            // Przypadek 2: Powiązanie z produktem Allegro
                            else if (assignment.AllegroProductId.HasValue && existingAllegroProductIds.Contains(assignment.AllegroProductId.Value))
                            {
                                var assignmentSql = "INSERT INTO ProductFlags (FlagId, ProductId, AllegroProductId) VALUES (@flagId, NULL, @allegroProductId)";
                                await _context.Database.ExecuteSqlRawAsync(assignmentSql,
                                    new Microsoft.Data.SqlClient.SqlParameter("@flagId", flagId),
                                    new Microsoft.Data.SqlClient.SqlParameter("@allegroProductId", assignment.AllegroProductId.Value)
                                );
                            }
                        }
                    }

                    await transaction.CommitAsync();
                }

                return Ok(new { success = true, message = "Zaimportowano pomyślnie." });
            }
            catch (Exception ex)
            {
                // Używamy
                if (_context.Database.CurrentTransaction != null)
                {
                    await _context.Database.CurrentTransaction.RollbackAsync();
                }
                return StatusCode(500, $"Wystąpił wewnętrzny błąd serwera: {ex.Message}");
            }
        }







    }
    public class FlagExportModel
    {
        public List<FlagDefinition> Flags { get; set; }
        public List<FlagAssignment> Assignments { get; set; }
    }

    public class FlagDefinition
    {
        public string FlagName { get; set; }
        public string FlagColor { get; set; }
        public bool IsMarketplace { get; set; }
    }

    public class FlagAssignment
    {
        public string FlagName { get; set; }
        public int? ProductId { get; set; }
        public int? AllegroProductId { get; set; }
    }

}
