using Heat_Lead.Models;
using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace Heat_Lead.Areas.Identity.Data;

public class Heat_LeadUser : IdentityUser
{
    public string PartnerName { get; set; }
    public string PartnerSurname { get; set; }
    public string CodePAR { get; set; }
    public DateTime CreationDate { get; set; } = DateTime.Now;
    public Wallet Wallet { get; set; }
    public bool IsMember { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public AffiliateVerification AffiliateVerification { get; set; }

    public ICollection<Generator> Generator { get; set; }
    public ICollection<AffiliateLink> AffiliateLink { get; set; }
    public virtual ICollection<Paycheck> Paycheck { get; set; }
    public virtual ICollection<CanvasJSStyle> CanvasJSStyles { get; set; } 

    public Heat_LeadUser()
    {
        CodePAR = GenerateUniqueCodePAR();
        CanvasJSStyles = new HashSet<CanvasJSStyle>(); 
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
