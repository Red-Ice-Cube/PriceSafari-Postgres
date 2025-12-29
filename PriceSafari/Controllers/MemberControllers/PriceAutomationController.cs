using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Manager")]
    public class PriceAutomationController : Controller
    {
        private readonly PriceSafariContext _context;

        public PriceAutomationController(PriceSafariContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            // 1. Pobierz regułę
            var rule = await _context.AutomationRules
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null) return NotFound("Nie znaleziono reguły.");

            // 2. Przygotuj ViewModel nagłówka
            var model = new AutomationDetailsViewModel
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                RuleColor = rule.ColorHex,
                SourceType = rule.SourceType,
                StrategyMode = rule.StrategyMode,
                IsActive = rule.IsActive,
                StoreId = rule.StoreId,
                StoreName = rule.Store?.StoreName ?? "Nieznany sklep"
            };

            // 3. Rozgałęzienie logiki w zależności od źródła
            if (rule.SourceType == AutomationSourceType.PriceComparison)
            {
                await PreparePriceComparisonData(rule, model);
            }
            else if (rule.SourceType == AutomationSourceType.Marketplace)
            {
                await PrepareMarketplaceData(rule, model);
            }

            return View("~/Views/ManagerPanel/PriceAutomation/Details.cshtml", model);
        }

        // --- Logika dla Google/Ceneo ---
        private async Task PreparePriceComparisonData(AutomationRule rule, AutomationDetailsViewModel model)
        {
            // a) Pobierz najnowszy scrap history ID dla tego sklepu
            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == rule.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            model.LastScrapDate = latestScrap?.Date;

            // b) Pobierz produkty przypisane do tej reguły
            var assignments = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.Id && a.ProductId.HasValue)
                .Include(a => a.Product)
                .ToListAsync();

            if (!assignments.Any()) return;

            var productIds = assignments.Select(a => a.ProductId.Value).ToList();
            int scrapId = latestScrap?.Id ?? 0;

            // c) Pobierz historię cen z ostatniego scrapowania dla tych produktów
            var priceHistories = new List<PriceHistoryClass>();
            if (scrapId > 0)
            {
                priceHistories = await _context.PriceHistories
                    .Where(ph => ph.ScrapHistoryId == scrapId && productIds.Contains(ph.ProductId))
                    .ToListAsync();
            }

            // d) Mapowanie do ViewModelu
            foreach (var item in assignments)
            {
                var p = item.Product;
                if (p == null) continue;

                var histories = priceHistories.Where(h => h.ProductId == p.ProductId).ToList();

                // Znajdź naszą ofertę
                // (Uproszczenie: szukamy po nazwie sklepu, zakładając że jest w bazie, lub bierzemy z Product table)
                var myHistory = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(rule.Store.StoreName, StringComparison.OrdinalIgnoreCase))
                                ?? histories.FirstOrDefault(h => h.StoreName == null); // Fallback

                // Znajdź najlepszą konkurencję
                var competitors = histories.Where(h => h != myHistory && h.Price > 0).OrderBy(h => h.Price).ToList();
                var bestCompetitor = competitors.FirstOrDefault();

                // Oblicz średnią (dla strategii Profit)
                decimal? marketAvg = null;
                if (competitors.Any())
                {
                    marketAvg = competitors.Average(c => c.Price);
                }

                var row = new AutomationProductRowViewModel
                {
                    ProductId = p.ProductId,
                    Name = p.ProductName,
                    ImageUrl = p.MainUrl,
                    Identifier = p.Ean,
                    CurrentPrice = myHistory?.Price, // Cena ze scrapu
                    PurchasePrice = p.MarginPrice,
                    BestCompetitorPrice = bestCompetitor?.Price,
                    CompetitorName = bestCompetitor?.StoreName,
                    MarketAveragePrice = marketAvg,
                    IsInStock = true // Tu można dodać logikę sprawdzania InStock z historii
                };

                // Ranking
                if (myHistory != null && myHistory.Position.HasValue)
                    row.CurrentRanking = $"{myHistory.Position}/{competitors.Count + 1}";
                else
                    row.CurrentRanking = "-";

                // Symulacja Ceny (Uproszczona)
                CalculateSuggestedPrice(rule, row);

                model.Products.Add(row);
            }

            model.TotalProducts = model.Products.Count;
        }

        // --- Logika dla Allegro ---
        private async Task PrepareMarketplaceData(AutomationRule rule, AutomationDetailsViewModel model)
        {
            var latestScrap = await _context.AllegroScrapeHistories
               .Where(sh => sh.StoreId == rule.StoreId)
               .OrderByDescending(sh => sh.Date)
               .Select(sh => new { sh.Id, sh.Date })
               .FirstOrDefaultAsync();

            model.LastScrapDate = latestScrap?.Date;

            var assignments = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.Id && a.AllegroProductId.HasValue)
                .Include(a => a.AllegroProduct)
                .ToListAsync();

            if (!assignments.Any()) return;

            var productIds = assignments.Select(a => a.AllegroProductId.Value).ToList();
            int scrapId = latestScrap?.Id ?? 0;

            var priceHistories = new List<AllegroPriceHistory>();
            if (scrapId > 0)
            {
                priceHistories = await _context.AllegroPriceHistories
                    .Where(ph => ph.AllegroScrapeHistoryId == scrapId && productIds.Contains(ph.AllegroProductId))
                    .ToListAsync();
            }

            foreach (var item in assignments)
            {
                var p = item.AllegroProduct;
                if (p == null) continue;

                var histories = priceHistories.Where(h => h.AllegroProductId == p.AllegroProductId).ToList();

                // Znajdź naszą ofertę (po nazwie sklepu Allegro)
                var myHistory = histories.FirstOrDefault(h => h.SellerName != null && h.SellerName.Equals(rule.Store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase));

                var competitors = histories.Where(h => h != myHistory && h.Price > 0).OrderBy(h => h.Price).ToList();
                var bestCompetitor = competitors.FirstOrDefault();

                decimal? marketAvg = null;
                if (competitors.Any())
                {
                    marketAvg = competitors.Average(c => c.Price);
                }

                var row = new AutomationProductRowViewModel
                {
                    ProductId = p.AllegroProductId,
                    Name = p.AllegroProductName,
                    ImageUrl = null, // Allegro products często nie mają URL w głównej tabeli w tym modelu, chyba że dodasz
                    Identifier = p.AllegroOfferUrl, // ID Oferty
                    CurrentPrice = myHistory?.Price,
                    PurchasePrice = p.AllegroMarginPrice,
                    BestCompetitorPrice = bestCompetitor?.Price,
                    CompetitorName = bestCompetitor?.SellerName,
                    MarketAveragePrice = marketAvg,
                    IsInStock = true
                };

                // Ranking (wyliczamy dynamicznie, bo AllegroPriceHistory nie ma pola Position wprost w tym modelu, ale można policzyć index)
                if (myHistory != null)
                {
                    var allPrices = competitors.Select(c => c.Price).Concat(new[] { myHistory.Price }).OrderBy(x => x).ToList();
                    var myRank = allPrices.IndexOf(myHistory.Price) + 1;
                    row.CurrentRanking = $"{myRank}/{allPrices.Count}";
                }
                else
                {
                    row.CurrentRanking = "-";
                }

                CalculateSuggestedPrice(rule, row);
                model.Products.Add(row);
            }

            model.TotalProducts = model.Products.Count;
        }

        // --- Wspólna logika wyliczania sugerowanej ceny ---
        private void CalculateSuggestedPrice(AutomationRule rule, AutomationProductRowViewModel row)
        {
            decimal suggested = row.CurrentPrice ?? 0;
            bool calculationPossible = false;

            // 1. Wylicz cenę bazową wg Strategii
            if (rule.StrategyMode == AutomationStrategyMode.Competitiveness)
            {
                // Strategia Lidera
                if (row.BestCompetitorPrice.HasValue)
                {
                    if (rule.IsPriceStepPercent)
                    {
                        // Np. przebij o 1%
                        suggested = row.BestCompetitorPrice.Value + (row.BestCompetitorPrice.Value * (rule.PriceStep / 100));
                    }
                    else
                    {
                        // Np. przebij o 0.01 PLN (rule.PriceStep usually negative like -0.01)
                        suggested = row.BestCompetitorPrice.Value + rule.PriceStep;
                    }
                    calculationPossible = true;
                }
            }
            else if (rule.StrategyMode == AutomationStrategyMode.Profit)
            {
                // Strategia Rentowności (Target Index względem średniej)
                if (row.MarketAveragePrice.HasValue && row.MarketAveragePrice.Value > 0)
                {
                    suggested = row.MarketAveragePrice.Value * (rule.PriceIndexTargetPercent / 100);
                    calculationPossible = true;
                }
            }

            if (!calculationPossible)
            {
                row.SuggestedPrice = null;
                return;
            }

            // 2. Strażnicy Marży (Min/Max)

            // Minimalna marża (Podłoga)
            if (rule.EnforceMinimalMargin && row.PurchasePrice.HasValue)
            {
                decimal minLimit;
                if (rule.IsMinimalMarginPercent)
                    minLimit = row.PurchasePrice.Value + (row.PurchasePrice.Value * (rule.MinimalMarginValue / 100));
                else
                    minLimit = row.PurchasePrice.Value + rule.MinimalMarginValue;

                row.MinPriceLimit = minLimit; // Dla informacji w widoku

                if (suggested < minLimit)
                {
                    suggested = minLimit;
                    row.IsMarginWarning = true; // Oznaczamy, że cena została podbita przez limit
                }
            }

            // Maksymalna marża (Sufit)
            if (rule.EnforceMaxMargin && row.PurchasePrice.HasValue)
            {
                decimal maxLimit;
                if (rule.IsMaxMarginPercent)
                    maxLimit = row.PurchasePrice.Value + (row.PurchasePrice.Value * (rule.MaxMarginValue / 100));
                else
                    maxLimit = row.PurchasePrice.Value + rule.MaxMarginValue;

                if (suggested > maxLimit)
                {
                    suggested = maxLimit;
                }
            }

            // Marketplace: Opcjonalnie tutaj można dodać logikę doliczania prowizji (jeśli włączona w regule)
            // if (rule.SourceType == AutomationSourceType.Marketplace && rule.MarketplaceIncludeCommission) { ... }

            row.SuggestedPrice = Math.Round(suggested, 2);

            if (row.CurrentPrice.HasValue)
            {
                row.PriceChange = row.SuggestedPrice.Value - row.CurrentPrice.Value;
            }
        }
    }
}