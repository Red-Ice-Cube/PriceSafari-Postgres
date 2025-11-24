using Microsoft.AspNetCore.Identity;
using PriceSafari.Models;
using System.ComponentModel.DataAnnotations;

public enum UserStatus
{

    PendingEmailVerification,
    PendingSetup,
    AwaitingAdminApproval,

    Active,
    Inactive,
    Onboarding
}

public class PriceSafariUser : IdentityUser
{

    public string PartnerName { get; set; }
    public string PartnerSurname { get; set; }
    public string CodePAR { get; set; }
    public DateTime CreationDate { get; set; } = DateTime.Now;
    public bool IsMember { get; set; }
    public bool IsActive { get; set; }
    public AffiliateVerification AffiliateVerification { get; set; }
    public ICollection<PriceSafariUserStore> UserStores { get; set; } = new List<PriceSafariUserStore>();

    public bool AccesToViewSafari { get; set; } = false;
    public bool AccesToCreateSafari { get; set; } = false;
    public bool AccesToViewMargin { get; set; } = false;
    public bool AccesToSetMargin { get; set; } = false;
    public DateTime? LastLoginDateTime { get; set; }
    public int LoginCount { get; set; } = 0;

    [Required]
    public UserStatus Status { get; set; } = UserStatus.Onboarding;

    public string? VerificationCode { get; set; }
    public DateTime? VerificationCodeExpires { get; set; }

    public string? PendingStoreNameCeneo { get; set; }
    public string? PendingStoreNameGoogle { get; set; }
    public string? PendingCeneoFeedUrl { get; set; }
    public string? PendingGoogleFeedUrl { get; set; }

    public DateTime? CeneoFeedSubmittedOn { get; set; }
    public DateTime? GoogleFeedSubmittedOn { get; set; }

    public string? PendingStoreNameAllegro { get; set; }
    public DateTime? AllegroSubmittedOn { get; set; }

    public PriceSafariUser()
    {
        CodePAR = GenerateUniqueCodePAR();
    }

    private string GenerateUniqueCodePAR()
    {

        var length = 6;
        var random = new Random();
        var chars = Enumerable.Repeat("QWERTYUIOPASDFGHJKLZXCVBNMqwertyuiopasdfghjklzxcvbnm1234567890", length)
                              .Select(s => s[random.Next(s.Length)]).ToArray();
        return new string(chars);
    }

    public virtual ICollection<UserMessage> UserMessages { get; set; } = new List<UserMessage>();

}