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
        public bool IsCurrentlyArmed
        {
            get
            {
                if (!IsEffectivelyActive) return false;
                var now = DateTime.Now;
                int dayIndex = ((int)now.DayOfWeek + 6) % 7;
                int slotIndex = (now.Hour * 60 + now.Minute) / 10;
                int stepIdx = GetSlotStepIndex(dayIndex, slotIndex);
                if (stepIdx == 0) return false;
                // Slot musi należeć do AKTYWNEGO kroku
                return IsStepActive(stepIdx);
            }
        }
    }
}