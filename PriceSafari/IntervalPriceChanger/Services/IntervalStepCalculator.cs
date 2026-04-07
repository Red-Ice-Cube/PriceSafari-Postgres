namespace PriceSafari.IntervalPriceChanger.Services
{
    /// <summary>
    /// Czysta (bezstanowa) logika decyzyjna interwału.
    /// Jeden punkt prawdy — używany i przez UI (preview), i przez executor (wykonanie).
    /// 
    /// ZASADA: Jakakolwiek zmiana w regułach kalkulacji kroku → TYLKO tutaj.
    /// Dzięki temu preview w UI ZAWSZE pokazuje dokładnie to, co executor zrobi.
    /// </summary>
    public static class IntervalStepCalculator
    {
        public enum Decision
        {
            /// <summary>Wykonaj zmianę na TargetPrice.</summary>
            Execute,
            /// <summary>Cena już poza limitem w kierunku niewłaściwym dla kroku — pomiń.</summary>
            BlockedLimitReached,
            /// <summary>Krok=0 po zaokrągleniu — nic do zrobienia.</summary>
            NoChangeNeeded
        }

        public class Result
        {
            public Decision Decision { get; set; }
            public decimal TargetPrice { get; set; }
            public decimal PriceChange { get; set; }
            public bool LimitedByMin { get; set; }
            public bool LimitedByMax { get; set; }
            public string Reason { get; set; }
        }

        /// <summary>
        /// Policz co zrobić z ceną.
        /// </summary>
        /// <param name="currentPrice">Aktualna cena produktu (z API lub LastKnown w preview)</param>
        /// <param name="stepValue">Wartość kroku (ujemna = obniżka, dodatnia = podwyżka)</param>
        /// <param name="stepIsPercent">Czy krok jest w % (true) czy w PLN (false)</param>
        /// <param name="minLimit">Dolny limit ceny (null = brak)</param>
        /// <param name="maxLimit">Górny limit ceny (null = brak)</param>
        public static Result Calculate(
            decimal currentPrice,
            decimal stepValue,
            bool stepIsPercent,
            decimal? minLimit,
            decimal? maxLimit)
        {
            var result = new Result();

            // ── OCHRONA KIERUNKOWA ──
            // Obniżka nie może podnieść, podwyżka nie może obniżyć.
            // Jeśli cena już jest poza limitem w kierunku niewłaściwym → pomiń.
            if (stepValue < 0 && minLimit.HasValue && currentPrice <= minLimit.Value)
            {
                result.Decision = Decision.BlockedLimitReached;
                result.TargetPrice = currentPrice;
                result.PriceChange = 0;
                result.Reason = "Cena ≤ Min (obniżka pominięta)";
                return result;
            }
            if (stepValue > 0 && maxLimit.HasValue && currentPrice >= maxLimit.Value)
            {
                result.Decision = Decision.BlockedLimitReached;
                result.TargetPrice = currentPrice;
                result.PriceChange = 0;
                result.Reason = "Cena ≥ Max (podwyżka pominięta)";
                return result;
            }

            // ── KALKULACJA ──
            decimal step = stepIsPercent
                ? currentPrice * (stepValue / 100m)
                : stepValue;

            decimal targetPrice = Math.Round(currentPrice + step, 2);

            // ── KLEMOWANIE DO LIMITÓW (bezpieczne — ochrona kierunkowa już zadziałała) ──
            if (minLimit.HasValue && targetPrice < minLimit.Value)
            {
                targetPrice = minLimit.Value;
                result.LimitedByMin = true;
            }
            if (maxLimit.HasValue && targetPrice > maxLimit.Value)
            {
                targetPrice = maxLimit.Value;
                result.LimitedByMax = true;
            }

            decimal priceChange = Math.Round(targetPrice - currentPrice, 2);

            if (priceChange == 0)
            {
                result.Decision = Decision.NoChangeNeeded;
                result.TargetPrice = currentPrice;
                result.PriceChange = 0;
                result.Reason = "Krok=0 po zaokrągleniu";
                return result;
            }

            result.Decision = Decision.Execute;
            result.TargetPrice = targetPrice;
            result.PriceChange = priceChange;
            return result;
        }
    }
}