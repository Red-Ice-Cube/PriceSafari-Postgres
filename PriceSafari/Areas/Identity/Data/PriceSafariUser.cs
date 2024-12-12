using Microsoft.AspNetCore.Identity;
using PriceSafari.Models;

public class PriceSafariUser : IdentityUser
{
    public string PartnerName { get; set; }
    public string PartnerSurname { get; set; }
    public string CodePAR { get; set; }
    public DateTime CreationDate { get; set; } = DateTime.Now;

    public bool IsMember { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public AffiliateVerification AffiliateVerification { get; set; }

    public ICollection<PriceSafariUserStore> UserStores { get; set; } = new List<PriceSafariUserStore>();
    public ICollection<UserPaymentData> UserPaymentDatas { get; set; } = new List<UserPaymentData>();

 


    public bool AccesToViewSafari {  get; set; } = false;
    public bool AccesToCreateSafari {  get; set; } = false;


    public bool AccesToViewMargin { get; set; } = false;
    public bool AccesToSetMargin { get; set; } = false;


    public DateTime? LastLoginDateTime { get; set; } // data ostatniego logowania
    public int LoginCount { get; set; } = 0;


    public PriceSafariUser()
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
