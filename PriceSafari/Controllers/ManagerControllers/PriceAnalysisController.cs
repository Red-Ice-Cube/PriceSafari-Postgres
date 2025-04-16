using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class PriceAnalysisController : Controller
    {
        private readonly PriceSafariContext _context;

        public PriceAnalysisController(PriceSafariContext context)
        {
            _context = context;
        }

      
        public async Task<IActionResult> Index(int? storeId)
        {
            if (storeId == null)
                return NotFound("Store ID not provided.");

            // Pobieramy nazwę naszego sklepu
            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();
            if (storeName == null)
                return NotFound("Sklep nie został znaleziony.");

            // Używamy lowercase do porównań (w PriceHistories)
            string myStoreName = storeName.ToLower();

            // 1. Pobieramy ostatnie 30 scrapowań dla danego sklepu
            var last30ScrapHistories = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(30)
                .Select(sh => new { sh.Id, sh.Date })
                .ToListAsync();
            // Sortujemy rosnąco wg daty, aby później łatwo porównywać dni
            last30ScrapHistories = last30ScrapHistories.OrderBy(sh => sh.Date).ToList();

            // 2. Inicjujemy słownik dla każdego dnia – nawet gdy nie ma zmian
            Dictionary<DateTime, DailyPriceChangeGroup> dailyChangeData = new Dictionary<DateTime, DailyPriceChangeGroup>();
            // Inicjujemy też słownik dla produktów z rozbieżnymi cenami (ambiguous)
            Dictionary<DateTime, List<PriceAmbiguityDetail>> ambiguousByDate = new Dictionary<DateTime, List<PriceAmbiguityDetail>>();

            foreach (var scrap in last30ScrapHistories)
            {
                var date = scrap.Date.Date;
                if (!dailyChangeData.ContainsKey(date))
                {
                    dailyChangeData[date] = new DailyPriceChangeGroup
                    {
                        Date = date,
                        PriceRaisedCount = 0,
                        PriceLoweredCount = 0,
                        RaisedDetails = new List<PriceChangeDetail>(),
                        LoweredDetails = new List<PriceChangeDetail>(),
                        AmbiguousProducts = new List<PriceAmbiguityDetail>()
                    };
                }
                if (!ambiguousByDate.ContainsKey(date))
                {
                    ambiguousByDate[date] = new List<PriceAmbiguityDetail>();
                }
            }

            // 3. Pobieramy rekordy PriceHistories dla wybranych scrapowań, tylko dla naszego sklepu
            var priceHistoryRecords = new List<PriceRecord>();
            foreach (var scrap in last30ScrapHistories)
            {
                var histories = await _context.PriceHistories
                    .Where(ph => ph.ScrapHistoryId == scrap.Id && ph.StoreName.ToLower() == myStoreName)
                    .Select(ph => new PriceRecord
                    {
                        ProductId = ph.ProductId,
                        Price = ph.Price,
                        ScrapHistoryId = ph.ScrapHistoryId,
                        ProductName = ph.Product.ProductName
                    })
                    .ToListAsync();
                priceHistoryRecords.AddRange(histories);
            }

            // 4. Łączymy dane – do każdego rekordu dołączamy datę scrapowania
            var extendedRecords = new List<ExtendedPriceRecord>();
            foreach (var rec in priceHistoryRecords)
            {
                var scrap = last30ScrapHistories.FirstOrDefault(s => s.Id == rec.ScrapHistoryId);
                if (scrap != null)
                {
                    extendedRecords.Add(new ExtendedPriceRecord
                    {
                        ProductId = rec.ProductId,
                        Price = rec.Price,
                        Date = scrap.Date.Date,
                        ProductName = rec.ProductName,
                        ScrapHistoryId = scrap.Id
                    });
                }
            }

      
            var priceChangeDetails = new List<PriceChangeDetail>();
            foreach (var productGroup in extendedRecords.GroupBy(r => r.ProductId))
            {
                // Grupujemy wpisy danego produktu wg daty
                var dailyRecords = productGroup
                    .GroupBy(r => r.Date)
                    .Select(g => new
                    {
                        Date = g.Key,
                        DistinctPrices = g.Select(r => r.Price).Distinct().OrderBy(p => p).ToList(),
                        ProductName = g.First().ProductName,
                        ScrapId = g.First().ScrapHistoryId
                    })
                    .OrderBy(x => x.Date)
                    .ToList();

                decimal? previousEffectivePrice = null;
                foreach (var day in dailyRecords)
                {
                    // Jeśli w danym dniu pojawia się więcej niż jedna unikalna cena, traktujemy to jako ambiguous
                    if (day.DistinctPrices.Count > 1)
                    {
                        ambiguousByDate[day.Date].Add(new PriceAmbiguityDetail
                        {
                            ProductId = productGroup.Key,
                            ProductName = day.ProductName,
                            Prices = day.DistinctPrices,
                            ScrapId = day.ScrapId
                        });
                        // W tym dniu nie aktualizujemy efektywnej ceny – pomijamy ten wpis
                        continue;
                    }
                    // W przeciwnym razie efektywna cena to jedyna wartość
                    decimal newEffectivePrice = day.DistinctPrices.First();

                    // Jeśli to nie pierwszy dzień i efektywna cena zmieniła się, rejestrujemy zmianę
                    if (previousEffectivePrice.HasValue && newEffectivePrice != previousEffectivePrice.Value)
                    {
                        priceChangeDetails.Add(new PriceChangeDetail
                        {
                            Date = day.Date,
                            ProductId = productGroup.Key,
                            ProductName = day.ProductName,
                            OldPrice = previousEffectivePrice.Value,
                            NewPrice = newEffectivePrice,
                            PriceDifference = newEffectivePrice - previousEffectivePrice.Value,
                            ScrapId = day.ScrapId
                        });
                    }
                    previousEffectivePrice = newEffectivePrice;
                }
            }

            // 6. Grupujemy wszystkie PriceChangeDetail wg daty
            var detailsByDate = priceChangeDetails
                .GroupBy(d => d.Date)
                .ToDictionary(g => g.Key, g => new
                {
                    RaisedCount = g.Count(d => d.PriceDifference > 0),
                    LoweredCount = g.Count(d => d.PriceDifference < 0),
                    RaisedDetails = g.Where(d => d.PriceDifference > 0).ToList(),
                    LoweredDetails = g.Where(d => d.PriceDifference < 0).ToList()
                });
            foreach (var date in dailyChangeData.Keys.ToList())
            {
                if (detailsByDate.ContainsKey(date))
                {
                    dailyChangeData[date].PriceRaisedCount = detailsByDate[date].RaisedCount;
                    dailyChangeData[date].PriceLoweredCount = detailsByDate[date].LoweredCount;
                    dailyChangeData[date].RaisedDetails = detailsByDate[date].RaisedDetails;
                    dailyChangeData[date].LoweredDetails = detailsByDate[date].LoweredDetails;
                }
                // Dołączamy także listę produktów ambiguous (jeśli jakieś wystąpiły)
                if (ambiguousByDate.ContainsKey(date))
                {
                    dailyChangeData[date].AmbiguousProducts = ambiguousByDate[date];
                }
            }

            // 7. Sortujemy wyniki wg daty
            var groupedByDate = dailyChangeData.Values.OrderBy(g => g.Date).ToList();

            ViewBag.StoreName = storeName;
            ViewBag.StoreId = storeId;
            return View("~/Views/ManagerPanel/PriceAnalysis/Index.cshtml", groupedByDate);
        }

        #region Klasy pomocnicze (można przenieść do osobnego pliku ViewModels)

        private class PriceRecord
        {
            public int ProductId { get; set; }
            public decimal Price { get; set; }
            public int ScrapHistoryId { get; set; }
            public string ProductName { get; set; }
        }

        private class ExtendedPriceRecord
        {
            public int ProductId { get; set; }
            public decimal Price { get; set; }
            public DateTime Date { get; set; }
            public string ProductName { get; set; }
            public int ScrapHistoryId { get; set; }
        }

        // Klasa opisująca sytuację ambiguous – gdy w danym dniu dla produktu występuje więcej niż jedna cena
        public class PriceAmbiguityDetail
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public List<decimal> Prices { get; set; }
            public int ScrapId { get; set; }
        }

        public class PriceChangeDetail
        {
            public DateTime Date { get; set; }
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public decimal OldPrice { get; set; }
            public decimal NewPrice { get; set; }
            public decimal PriceDifference { get; set; }
            public int ScrapId { get; set; }
        }

        // Grupa zmian wg daty – zawiera liczbę zmian, listy szczegółowe oraz listę produktów z ambiguous cenami
        public class DailyPriceChangeGroup
        {
            public DateTime Date { get; set; }
            public int PriceRaisedCount { get; set; }
            public int PriceLoweredCount { get; set; }
            public List<PriceChangeDetail> RaisedDetails { get; set; }
            public List<PriceChangeDetail> LoweredDetails { get; set; }
            public List<PriceAmbiguityDetail> AmbiguousProducts { get; set; }
        }

        #endregion
    }
}
