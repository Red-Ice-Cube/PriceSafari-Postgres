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
        // KROK CENOWY — jedyne ustawienie cenowe interwału
        // ═══════════════════════════════════════════════════════

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceStep { get; set; } = -0.01m;

        public bool IsPriceStepPercent { get; set; } = false;

        // ═══════════════════════════════════════════════════════
        // HARMONOGRAM — JSON 7×144 (10-minutowe sloty)
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
            if (string.IsNullOrEmpty(ScheduleJson)) return false;
            try
            {
                var schedule = System.Text.Json.JsonSerializer.Deserialize<int[][]>(ScheduleJson);
                if (schedule == null || dayOfWeek < 0 || dayOfWeek > 6) return false;
                if (schedule.Length <= dayOfWeek) return false;
                var daySlots = schedule[dayOfWeek];
                if (daySlots == null || slotIndex < 0 || slotIndex >= daySlots.Length) return false;
                return daySlots[slotIndex] != 0;
            }
            catch { return false; }
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
                return IsSlotActive(dayIndex, slotIndex);
            }
        }
    }
}