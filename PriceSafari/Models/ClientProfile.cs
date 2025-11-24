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

        public string? CeneoProfileUrl { get; set; }

        [Required(ErrorMessage = "Nazwa firmy jest wymagana.")]
        public string CeneoProfileName { get; set; }

        [Required(ErrorMessage = "Email jest wymagany.")]
        public string CeneoProfileEmail { get; set; }

        public string? CeneoProfileTelephone { get; set; }

        public int? CeneoProfileProductCount { get; set; }

        public DateTime CreationDate { get; set; } = DateTime.Now;

        [ForeignKey("PriceSafariUser")]
        public string CreatedByUserId { get; set; }
        public PriceSafariUser CreatedByUser { get; set; }

        public ClientStatus Status { get; set; } = ClientStatus.Nowy;

        public DateTime? ScheduledMeetingDate { get; set; }

        public int EmailSentCount { get; set; } = 0;
        public DateTime? LastEmailSentDate { get; set; }

        public ClientSource Source { get; set; } = ClientSource.Ceneo;

        public virtual ICollection<ContactLabel> Labels { get; set; } = new List<ContactLabel>();
    }

    public enum ClientSource
    {
        Ceneo,
        Google
    }
}