//using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;
//using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
//using PriceSafari.Models;

//namespace PriceSafari.IntervalPriceChanger.Models
//{
//    public class IntervalPriceRule
//    {
//        [Key]
//        public int Id { get; set; }

//        /// <summary>
//        /// Automat cenowy-rodzic. Interwał dziedziczy z niego:
//        /// StoreId, SourceType, limity min/max, prowizję, preset konkurencji itd.
//        /// </summary>
//        [Required]
//        public int AutomationRuleId { get; set; }

//        [ForeignKey("AutomationRuleId")]
//        [ValidateNever]
//        public virtual AutomationRule AutomationRule { get; set; }

//        [Required(ErrorMessage = "Nazwa jest wymagana")]
//        [StringLength(100)]
//        public string Name { get; set; }

//        [Required]
//        [StringLength(7)]
//        public string ColorHex { get; set; } = "#e67e22";

//        public bool IsActive { get; set; } = false;

//        // ═══════════════════════════════════════════════════════
//        // KROK CENOWY — jedyne ustawienie cenowe interwału
//        // ═══════════════════════════════════════════════════════

//        [Column(TypeName = "decimal(18,2)")]
//        public decimal PriceStep { get; set; } = -0.01m;

//        public bool IsPriceStepPercent { get; set; } = false;

//        // ═══════════════════════════════════════════════════════
//        // HARMONOGRAM — JSON 7×144 (10-minutowe sloty)
//        // ═══════════════════════════════════════════════════════

//        [Column(TypeName = "text")]
//        public string ScheduleJson { get; set; }

//        public int PreferredBlockSize { get; set; } = 10;

//        // ═══════════════════════════════════════════════════════
//        // NAWIGACJA
//        // ═══════════════════════════════════════════════════════

//        [ValidateNever]
//        public virtual ICollection<IntervalPriceProductAssignment> ProductAssignments { get; set; }

//        // ═══════════════════════════════════════════════════════
//        // COMPUTED (NotMapped)
//        // ═══════════════════════════════════════════════════════

//        [NotMapped]
//        public int StoreId => AutomationRule?.StoreId ?? 0;

//        [NotMapped]
//        public AutomationSourceType SourceType => AutomationRule?.SourceType ?? AutomationSourceType.Marketplace;

//        [NotMapped]
//        public bool ParentIsActive => AutomationRule?.IsActive ?? false;

//        [NotMapped]
//        public bool IsEffectivelyActive => IsActive && (AutomationRule?.CanExecute ?? false);

//        // ═══════════════════════════════════════════════════════
//        // HARMONOGRAM — HELPERY
//        // ═══════════════════════════════════════════════════════
//        [NotMapped]
//        public int ActiveSlotsCount
//        {
//            get
//            {
//                if (string.IsNullOrEmpty(ScheduleJson)) return 0;
//                try
//                {
//                    var schedule = System.Text.Json.JsonSerializer.Deserialize<int[][]>(ScheduleJson);
//                    if (schedule == null) return 0;
//                    return schedule.Sum(day => day?.Count(s => s != 0) ?? 0);
//                }
//                catch { return 0; }
//            }
//        }

//        public bool IsSlotActive(int dayOfWeek, int slotIndex)
//        {
//            if (string.IsNullOrEmpty(ScheduleJson)) return false;
//            try
//            {
//                var schedule = System.Text.Json.JsonSerializer.Deserialize<int[][]>(ScheduleJson);
//                if (schedule == null || dayOfWeek < 0 || dayOfWeek > 6) return false;
//                if (schedule.Length <= dayOfWeek) return false;
//                var daySlots = schedule[dayOfWeek];
//                if (daySlots == null || slotIndex < 0 || slotIndex >= daySlots.Length) return false;
//                return daySlots[slotIndex] != 0;
//            }
//            catch { return false; }
//        }

//        [NotMapped]
//        public bool IsCurrentlyArmed
//        {
//            get
//            {
//                if (!IsEffectivelyActive) return false;
//                var now = DateTime.Now;
//                int dayIndex = ((int)now.DayOfWeek + 6) % 7;
//                int slotIndex = (now.Hour * 60 + now.Minute) / 10;
//                return IsSlotActive(dayIndex, slotIndex);
//            }
//        }
//    }
//}



using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using PriceSafari.Models;

namespace PriceSafari.IntervalPriceChanger.Models
{
    public class IntervalPriceRule
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Automat cenowy-rodzic. Interwał dziedziczy z niego:
        /// StoreId, SourceType, limity min/max, prowizję, preset konkurencji itd.
        /// </summary>
        [Required]
        public int AutomationRuleId { get; set; }

        [ForeignKey("AutomationRuleId")]
        [ValidateNever]
        public virtual AutomationRule AutomationRule { get; set; }

        [Required(ErrorMessage = "Nazwa jest wymagana")]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [StringLength(7)]
        public string ColorHex { get; set; } = "#e67e22";

        public bool IsActive { get; set; } = false;

        // ═══════════════════════════════════════════════════════
        // KROKI CENOWE A / B / C — trzy niezależne kroki
        // Każdy slot harmonogramu należy do jednego z nich.
        // ═══════════════════════════════════════════════════════

        // ── Krok A (historyczne pola, zachowane dla wstecznej kompatybilności) ──
        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceStep { get; set; } = -0.01m;

        public bool IsPriceStepPercent { get; set; } = false;

        public bool IsStepAActive { get; set; } = true;

        // ── Krok B ──
        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceStepB { get; set; } = -0.01m;

        public bool IsPriceStepPercentB { get; set; } = false;

        public bool IsStepBActive { get; set; } = false;

        // ── Krok C ──
        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceStepC { get; set; } = -0.01m;

        public bool IsPriceStepPercentC { get; set; } = false;

        public bool IsStepCActive { get; set; } = false;

        // ═══════════════════════════════════════════════════════
        // HARMONOGRAM — JSON 7×144 (10-minutowe sloty)
        //
        // Format wartości w slocie:
        //   0                                  = pusty slot
        //   ±(stepIdx*100 + blockSize)         = blok danego kroku
        //     stepIdx: 1=A, 2=B, 3=C
        //     blockSize: 1..6 (10..60 minut)
        //   Wartość dodatnia = start bloku
        //   Wartość ujemna   = kontynuacja
        //
        // Stary format (±1..6 = krok A bez stepIdx) jest akceptowany
        // przez walidację i migrowany przy pierwszym zapisie z UI.
        // ═══════════════════════════════════════════════════════

        [Column(TypeName = "text")]
        public string ScheduleJson { get; set; }

        public int PreferredBlockSize { get; set; } = 10;

        // ═══════════════════════════════════════════════════════
        // NAWIGACJA
        // ═══════════════════════════════════════════════════════

        [ValidateNever]
        public virtual ICollection<IntervalPriceProductAssignment> ProductAssignments { get; set; }

        // ═══════════════════════════════════════════════════════
        // COMPUTED (NotMapped)
        // ═══════════════════════════════════════════════════════

        [NotMapped]
        public int StoreId => AutomationRule?.StoreId ?? 0;

        [NotMapped]
        public AutomationSourceType SourceType => AutomationRule?.SourceType ?? AutomationSourceType.Marketplace;

        [NotMapped]
        public bool ParentIsActive => AutomationRule?.IsActive ?? false;

        [NotMapped]
        public bool IsEffectivelyActive => IsActive && (AutomationRule?.CanExecute ?? false);

        // ═══════════════════════════════════════════════════════
        // KROKI — HELPERY
        // ═══════════════════════════════════════════════════════

        /// <summary>Czy podany krok (1=A, 2=B, 3=C) jest aktywny.</summary>
        public bool IsStepActive(int stepIdx) => stepIdx switch
        {
            1 => IsStepAActive,
            2 => IsStepBActive,
            3 => IsStepCActive,
            _ => false
        };

        /// <summary>Wartość kroku (PLN lub %) dla podanego indeksu.</summary>
        public decimal GetStepValue(int stepIdx) => stepIdx switch
        {
            1 => PriceStep,
            2 => PriceStepB,
            3 => PriceStepC,
            _ => 0m
        };

        /// <summary>Czy krok jest wyrażony w procentach.</summary>
        public bool IsStepPercent(int stepIdx) => stepIdx switch
        {
            1 => IsPriceStepPercent,
            2 => IsPriceStepPercentB,
            3 => IsPriceStepPercentC,
            _ => false
        };

        // ═══════════════════════════════════════════════════════
        // HARMONOGRAM — HELPERY
        // ═══════════════════════════════════════════════════════

        [NotMapped]
        public int ActiveSlotsCount
        {
            get
            {
                if (string.IsNullOrEmpty(ScheduleJson)) return 0;
                try
                {
                    var schedule = System.Text.Json.JsonSerializer.Deserialize<int[][]>(ScheduleJson);
                    if (schedule == null) return 0;
                    return schedule.Sum(day => day?.Count(s => s != 0) ?? 0);
                }
                catch { return 0; }
            }
        }

        public bool IsSlotActive(int dayOfWeek, int slotIndex)
        {
            return GetSlotStepIndex(dayOfWeek, slotIndex) > 0;
        }

        /// <summary>
        /// Zwraca indeks kroku (1=A, 2=B, 3=C) do którego należy slot,
        /// lub 0 jeśli slot jest pusty / poza zakresem / błąd parsowania.
        /// Obsługuje stary format (±1..6 → krok A).
        /// </summary>
        public int GetSlotStepIndex(int dayOfWeek, int slotIndex)
        {
            if (string.IsNullOrEmpty(ScheduleJson)) return 0;
            try
            {
                var schedule = System.Text.Json.JsonSerializer.Deserialize<int[][]>(ScheduleJson);
                if (schedule == null || dayOfWeek < 0 || dayOfWeek > 6) return 0;
                if (schedule.Length <= dayOfWeek) return 0;
                var daySlots = schedule[dayOfWeek];
                if (daySlots == null || slotIndex < 0 || slotIndex >= daySlots.Length) return 0;
                int v = daySlots[slotIndex];
                if (v == 0) return 0;
                int abs = Math.Abs(v);
                // Stary format: 1..6 = krok A
                if (abs >= 1 && abs <= 6) return 1;
                // Nowy format: stepIdx*100 + size
                int stepIdx = abs / 100;
                int size = abs % 100;
                if (stepIdx >= 1 && stepIdx <= 3 && size >= 1 && size <= 6) return stepIdx;
                return 0;
            }
            catch { return 0; }
        }

        [NotMapped]
        public bool IsCurrentlyArmed => CurrentArmedStep.armed;

        /// <summary>
        /// Zwraca surowy indeks kroku w slocie (1/2/3) lub 0 jeśli pusty.
        /// NIE sprawdza czy krok jest aktywny — tylko format.
        /// (alias do istniejącego GetSlotStepIndex dla czytelności)
        /// </summary>
        public int GetRawStepIndexAt(int dayOfWeek, int slotIndex)
            => GetSlotStepIndex(dayOfWeek, slotIndex);

        /// <summary>
        /// Szuka następnego wykonania w harmonogramie od "teraz".
        /// Pomija sloty należące do kroków wyłączonych (IsStep?Active=false).
        /// Zwraca (czas, stepIdx) lub null.
        /// </summary>
        public (DateTime time, int stepIdx)? FindNextActiveExecution(DateTime fromNow)
        {
            if (string.IsNullOrEmpty(ScheduleJson)) return null;

            int[][] schedule;
            try
            {
                schedule = System.Text.Json.JsonSerializer.Deserialize<int[][]>(ScheduleJson);
                if (schedule == null || schedule.Length != 7) return null;
            }
            catch { return null; }

            int currentDayIndex = ((int)fromNow.DayOfWeek + 6) % 7;
            int currentSlot = (fromNow.Hour * 60 + fromNow.Minute) / 10;

            for (int dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                int dayIdx = (currentDayIndex + dayOffset) % 7;
                var daySlots = schedule[dayIdx];
                if (daySlots == null || daySlots.Length != 144) continue;

                int startSlot = (dayOffset == 0) ? currentSlot + 1 : 0;

                for (int s = startSlot; s < 144; s++)
                {
                    int v = daySlots[s];
                    if (v <= 0) continue; // Start bloku = wartość dodatnia

                    int abs = Math.Abs(v);
                    int stepIdx;
                    if (abs >= 1 && abs <= 6) stepIdx = 1;              // legacy
                    else if (abs >= 101 && abs <= 106) stepIdx = 1;
                    else if (abs >= 201 && abs <= 206) stepIdx = 2;
                    else if (abs >= 301 && abs <= 306) stepIdx = 3;
                    else continue;

                    // Pomijaj kroki wyłączone
                    if (!IsStepActive(stepIdx)) continue;

                    var targetDate = fromNow.Date.AddDays(dayOffset);
                    int totalMinutes = s * 10;
                    return (targetDate.AddMinutes(totalMinutes), stepIdx);
                }
            }

            return null;
        }

        /// <summary>
        /// Override — czy interwał JEST uzbrojony w tym momencie.
        /// Slot musi istnieć I należeć do aktywnego kroku.
        /// (zastępuje starą wersję IsCurrentlyArmed — zachowaj tę nazwę, usuwając starą)
        /// </summary>
        [NotMapped]
        public (bool armed, int stepIdx) CurrentArmedStep
        {
            get
            {
                if (!IsEffectivelyActive) return (false, 0);
                var now = DateTime.Now;
                int dayIndex = ((int)now.DayOfWeek + 6) % 7;
                int slotIndex = (now.Hour * 60 + now.Minute) / 10;
                int stepIdx = GetSlotStepIndex(dayIndex, slotIndex);
                if (stepIdx == 0) return (false, 0);
                if (!IsStepActive(stepIdx)) return (false, stepIdx); // istnieje ale nieaktywny
                return (true, stepIdx);
            }
        }
    }
}