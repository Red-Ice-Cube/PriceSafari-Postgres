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

        /// <summary>
        /// Widok zbiorczy – dla danego sklepu pokazuje, ile produktów danego dnia miało cenę podniesioną i obniżoną.
        /// Każdy wiersz ma przycisk rozwijający szczegółowe dane (lista produktów i zmiana ceny).
        /// Jeśli danego dnia nie było zmian, dzień i tak jest wyświetlony z zerowymi wartościami.
        /// Dla każdego produktu, jeśli w obrębie jednego dnia mamy wiele wpisów (np. z różnych źródeł),
        /// wyznaczamy efektywną cenę dnia – jeśli choć jeden wpis różni się od ceny z poprzedniego dnia,
        /// przyjmujemy nową efektywną cenę (wybierając najczęściej występującą spośród tych, które się różnią).
        /// </summary>
        /// <param name="storeId">Identyfikator sklepu</param>
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

            // Używamy lowercase do porównań (naszych danych w PriceHistories)
            string myStoreName = storeName.ToLower();

            // 1. Pobieramy ostatnie 30 scrapowań dla danego sklepu
            var last30ScrapHistories = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(30)
                .Select(sh => new { sh.Id, sh.Date })
                .ToListAsync();
            // Aby później porównywać dni, sortujemy rosnąco
            last30ScrapHistories = last30ScrapHistories.OrderBy(sh => sh.Date).ToList();

            // 2. Inicjujemy słownik – dla każdej daty scrapowania (nawet bez zmian) tworzymy domyślny DailyPriceChangeGroup
            Dictionary<DateTime, DailyPriceChangeGroup> dailyChangeData = new Dictionary<DateTime, DailyPriceChangeGroup>();
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
                        LoweredDetails = new List<PriceChangeDetail>()
                    };
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
                        ProductName = rec.ProductName
                    });
                }
            }

            // 5. Dla każdego produktu wyznaczamy efektywną cenę dla każdego dnia
            //    (dla danego produktu grupujemy wpisy wg daty i zbieramy wszystkie ceny)
            //    Następnie, przeglądając kolejne dni, porównujemy efektywną cenę z poprzedniego dnia.
            //    Jeśli choć jeden wpis w danym dniu różni się od poprzedniej ceny, uznajemy, że cena uległa zmianie.
            var priceChangeDetails = new List<PriceChangeDetail>();
            foreach (var productGroup in extendedRecords.GroupBy(r => r.ProductId))
            {
                // Grupujemy dane dla produktu wg daty
                var dailyRecords = productGroup
                    .GroupBy(r => r.Date)
                    .Select(g => new
                    {
                        Date = g.Key,
                        // Tworzymy słownik wystąpień cen w danym dniu
                        PriceCounts = g.GroupBy(r => r.Price).ToDictionary(x => x.Key, x => x.Count()),
                        ProductName = g.First().ProductName
                    })
                    .OrderBy(x => x.Date)
                    .ToList();

                decimal? previousEffectivePrice = null;
                foreach (var day in dailyRecords)
                {
                    decimal newEffectivePrice;
                    if (!previousEffectivePrice.HasValue)
                    {
                        // Pierwszy dzień: jeśli tylko jedna cena, przyjmujemy ją;
                        // w przeciwnym razie wybieramy wartość, która występuje najczęściej (przy remisie – tę o mniejszej wartości)
                        if (day.PriceCounts.Count == 1)
                        {
                            newEffectivePrice = day.PriceCounts.Keys.First();
                        }
                        else
                        {
                            newEffectivePrice = day.PriceCounts
                                .OrderByDescending(kvp => kvp.Value)
                                .ThenBy(kvp => kvp.Key)
                                .First().Key;
                        }
                    }
                    else
                    {
                        // Jeśli wszystkie ceny w danym dniu są równe poprzedniej efektywnej cenie, brak zmiany
                        if (day.PriceCounts.Keys.All(price => price == previousEffectivePrice.Value))
                        {
                            newEffectivePrice = previousEffectivePrice.Value;
                        }
                        else
                        {
                            // Wśród tych, które są różne od poprzedniej ceny, wybieramy tę, która występuje najczęściej,
                            // przy remisie wybieramy tę o mniejszej wartości.
                            var newCandidates = day.PriceCounts.Where(kvp => kvp.Key != previousEffectivePrice.Value);
                            newEffectivePrice = newCandidates
                                .OrderByDescending(kvp => kvp.Value)
                                .ThenBy(kvp => kvp.Key)
                                .First().Key;
                        }
                    }

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
                            PriceDifference = newEffectivePrice - previousEffectivePrice.Value
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
            }

            // 7. Sortujemy wyniki wg daty
            var groupedByDate = dailyChangeData.Values.OrderBy(g => g.Date).ToList();

            ViewBag.StoreName = storeName;
            ViewBag.StoreId = storeId;
            return View("~/Views/ManagerPanel/PriceAnalysis/Index.cshtml", groupedByDate);
        }

        // Pomocnicze klasy – można je przenieść do osobnego pliku (np. ViewModels)

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
        }

        // Agregowany rekord ceny dla danego produktu i dnia – opcjonalny, jeśli chcesz używać innej metody agregacji
        private class AggregatedPriceRecord
        {
            public int ProductId { get; set; }
            public DateTime Date { get; set; }
            public decimal AggregatedPrice { get; set; }
            public string ProductName { get; set; }
        }

        public class PriceChangeDetail
        {
            public DateTime Date { get; set; }
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public decimal OldPrice { get; set; }
            public decimal NewPrice { get; set; }
            public decimal PriceDifference { get; set; }
        }

        // Agregacja zmian wg daty – zawiera liczbę zmian oraz szczegółowe listy
        public class DailyPriceChangeGroup
        {
            public DateTime Date { get; set; }
            public int PriceRaisedCount { get; set; }
            public int PriceLoweredCount { get; set; }
            public List<PriceChangeDetail> RaisedDetails { get; set; }
            public List<PriceChangeDetail> LoweredDetails { get; set; }
        }
    }
}
