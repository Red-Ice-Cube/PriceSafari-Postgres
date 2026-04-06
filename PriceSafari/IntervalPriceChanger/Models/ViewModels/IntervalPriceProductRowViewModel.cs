namespace PriceSafari.IntervalPriceChanger.Models.ViewModels
{
    public class IntervalPriceProductRowViewModel
    {
        // ═══════════════════════════════════════════════════════
        // PODSTAWOWE DANE PRODUKTU
        // ═══════════════════════════════════════════════════════
        public int ProductId { get; set; }
        public string Name { get; set; }
        public string Identifier { get; set; }   // EAN lub IdOnAllegro
        public string ImageUrl { get; set; }
        public List<int> FlagIds { get; set; } = new();

        // ═══════════════════════════════════════════════════════
        // CENA ZAKUPU + LIMITY (dziedziczone z automatu-rodzica)
        // ═══════════════════════════════════════════════════════
        public decimal? PurchasePrice { get; set; }
        public DateTime? PurchasePriceUpdatedDate { get; set; }
        public decimal? MinPriceLimit { get; set; }
        public decimal? MaxPriceLimit { get; set; }

        // ═══════════════════════════════════════════════════════
        // DANE Z OSTATNIEGO SCRAPU (sekcja "Dane z rynku")
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Aktualna cena z rynku — jeśli automat-rodzic już zmienił cenę
        /// w tym scrapie, to pokazujemy tę zmienioną cenę.
        /// </summary>
        public decimal? MarketCurrentPrice { get; set; }

        /// <summary>
        /// Cena bazowa z API (Allegro) — uwzględnia dopłaty/kampanie.
        /// </summary>
        public decimal? ApiAllegroPriceFromUser { get; set; }

        public decimal? BestCompetitorPrice { get; set; }
        public string CompetitorName { get; set; }

        // Rankingi
        public string CurrentRankingAllegro { get; set; }
        public string CurrentRankingGoogle { get; set; }
        public string CurrentRankingCeneo { get; set; }

        // Odznaki Allegro (z ostatniego scrapu)
        public bool IsBestPriceGuarantee { get; set; }
        public bool IsSuperPrice { get; set; }
        public bool IsTopOffer { get; set; }
        public bool CompetitorIsBestPriceGuarantee { get; set; }
        public bool CompetitorIsSuperPrice { get; set; }
        public bool CompetitorIsTopOffer { get; set; }

        // Kampanie / Dopłaty
        public bool IsSubsidyActive { get; set; }
        public bool IsInAnyCampaign { get; set; }

        // Prowizja
        public decimal? CommissionAmount { get; set; }
        public bool IsCommissionIncluded { get; set; }

        public bool HasCheaperOwnOffer { get; set; }

        // ═══════════════════════════════════════════════════════
        // AKTUALNA CENA W INTERWALE (z logu interwału — na razie puste)
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Ostatnia cena ustawiona przez system interwałowy.
        /// NULL jeśli interwał jeszcze nie wykonał żadnej akcji.
        /// </summary>
        public decimal? IntervalCurrentPrice { get; set; }

        /// <summary>
        /// Data ostatniej zmiany wykonanej przez interwał.
        /// </summary>
        public DateTime? IntervalLastChangeDate { get; set; }

        /// <summary>
        /// Ile zmian interwał już wykonał na tym produkcie.
        /// </summary>
        public int IntervalExecutedSteps { get; set; } = 0;

        // ═══════════════════════════════════════════════════════
        // NASTĘPNE WYKONANIE (wyliczane z harmonogramu)
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Cena bazowa do kalkulacji następnego kroku.
        /// Priorytet: IntervalCurrentPrice → committed price → MarketCurrentPrice
        /// </summary>
        public decimal? EffectiveCurrentPrice { get; set; }

        /// <summary>
        /// Cena po następnym kroku interwałowym.
        /// </summary>
        public decimal? ProjectedNextPrice { get; set; }

        /// <summary>
        /// Zmiana cenowa następnego kroku (ProjectedNextPrice - EffectiveCurrentPrice).
        /// </summary>
        public decimal? ProjectedPriceChange { get; set; }

        /// <summary>
        /// Data/godzina następnego zaplanowanego wykonania.
        /// NULL jeśli brak aktywnych slotów w harmonogramie.
        /// </summary>
        public DateTime? NextExecutionTime { get; set; }

        /// <summary>
        /// Czy następne wykonanie zostanie faktycznie zrealizowane
        /// (automat aktywny, brak blokady).
        /// </summary>
        public bool WillNextExecutionRun { get; set; }

        // ═══════════════════════════════════════════════════════
        // NARZUT / MARŻA
        // ═══════════════════════════════════════════════════════

        /// <summary>Narzut obecny (na EffectiveCurrentPrice)</summary>
        public decimal? CurrentMarkupAmount { get; set; }
        public decimal? CurrentMarkupPercent { get; set; }

        /// <summary>Narzut po następnym kroku (na ProjectedNextPrice)</summary>
        public decimal? ProjectedMarkupAmount { get; set; }
        public decimal? ProjectedMarkupPercent { get; set; }

        // ═══════════════════════════════════════════════════════
        // STATUS
        // ═══════════════════════════════════════════════════════
        public IntervalProductStatus Status { get; set; } = IntervalProductStatus.Ready;
        public string BlockReason { get; set; }

        /// <summary>
        /// Czy projected price jest ograniczona limitem min/max.
        /// </summary>
        public bool IsLimitedByMin { get; set; }
        public bool IsLimitedByMax { get; set; }

        /// <summary>
        /// Ostrzeżenie o marży (narzut ujemny lub poniżej minimum).
        /// </summary>
        public bool IsMarginWarning { get; set; }


        // ═══ OSTATNIA ZNANA CENA ═══

        /// <summary>
        /// Najnowsza znana cena — z interwału, automatu lub scrapu.
        /// </summary>
        public decimal? LastKnownPrice { get; set; }

        /// <summary>
        /// Data ostatniej znanej ceny.
        /// </summary>
        public DateTime? LastKnownPriceDate { get; set; }

        /// <summary>
        /// Skąd pochodzi ostatnia znana cena.
        /// </summary>
        public LastKnownPriceSource LastKnownSource { get; set; } = LastKnownPriceSource.None;
    }


    public enum LastKnownPriceSource
    {
        None,
        Interval,   // Z logu interwału
        Automation, // Z głównego automatu (committed)
        Market      // Z ostatniego scrapu
    }
    public enum IntervalProductStatus
    {
        Ready,          // Gotowy do wykonania
        Blocked,        // Blokada — brak ceny zakupu, brak min limitu, brak danych ze scrapu itp.
        LimitReached,   // Cena dotarła do limitu min/max
        Paused          // Automat wyłączony lub harmonogram pusty
    }
}