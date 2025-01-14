using PriceSafari.Models;

public class SendEmailViewModel
{
    public List<ClientProfile> Clients { get; set; }
    public List<int> SelectedClientIds { get; set; }
    public string EmailSubject { get; set; }
    public string EmailContent { get; set; }

    // Nowe pole: który mail wybieramy (1,2,3)
    public int SelectedMailType { get; set; }
}
