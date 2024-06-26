using PriceTracker.Models;
using Microsoft.AspNetCore.Identity;


namespace PriceTracker.Areas.Identity.Data;

public class PriceTrackerUser : IdentityUser
{
    public string PartnerName { get; set; }
    public string PartnerSurname { get; set; }
    public string CodePAR { get; set; }
    public DateTime CreationDate { get; set; } = DateTime.Now;

    public bool IsMember { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public AffiliateVerification AffiliateVerification { get; set; }



    public PriceTrackerUser()
    {
        CodePAR = GenerateUniqueCodePAR();

    }

    private string GenerateUniqueCodePAR()
    {
        var length = 5;
        var random = new Random();
        var chars = Enumerable.Repeat("QWERTYUIOPASDFGHJKLZXCVBNMqwertyuiopasdfghjklzxcvbnm1234567890", length)
                              .Select(s => s[random.Next(s.Length)]).ToArray();
        return new string(chars);
    }
}
