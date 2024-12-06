using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public enum ClientStatus
    {
        Nowy,
        Mail,
        Urabiany,
        Spotkanie,
        Testuje,
        Frajer,
        ZaDużaRyba,
        Płotka,
        Ojebane
    }

    public class ClientProfile
    {
        [Key]
        public int ClientProfileId { get; set; }

        public string CeneoProfileUrl { get; set; }
        public string CeneoProfileName { get; set; }
        public string CeneoProfileEmail { get; set; }
        public string CeneoProfileTelephone { get; set; }
        public int CeneoProfileProductCount { get; set; }

        public DateTime CreationDate { get; set; } = DateTime.Now;

        [ForeignKey("PriceSafariUser")]
        public string CreatedByUserId { get; set; }
        public PriceSafariUser CreatedByUser { get; set; }

        public ClientStatus Status { get; set; } = ClientStatus.Nowy;

        public DateTime? ScheduledMeetingDate { get; set; }

        // New properties
        public int EmailSentCount { get; set; } = 0; // Default to 0
        public DateTime? LastEmailSentDate { get; set; }
    }
}
