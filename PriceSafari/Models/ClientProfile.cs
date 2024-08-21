using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public enum ClientStatus
    {
        NowyKontakt,
        WysłanoMaila,
        UzyskanoOdpowiedź,
        UmówionoSpotkanie,
        KlientTestujeOprogramowanie,  
        KlientZakupilOprogramowanie
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

        public ClientStatus Status { get; set; } = ClientStatus.NowyKontakt;

        public DateTime? ScheduledMeetingDate { get; set; }
    }
}
