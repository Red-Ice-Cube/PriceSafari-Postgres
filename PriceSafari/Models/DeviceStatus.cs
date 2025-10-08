namespace PriceSafari.Models
{
    public class DeviceStatus
    {
        public int Id { get; set; }


        public string DeviceName { get; set; }

      
        public bool IsOnline { get; set; }

    
        public DateTime LastCheck { get; set; }

    
        public bool BaseScalEnabled { get; set; }
        public bool UrlScalEnabled { get; set; }
        public bool GooCrawEnabled { get; set; }
        public bool CenCrawEnabled { get; set; }
        public bool AleBaseScalEnabled { get; set; }
        public bool UrlScalAleEnabled { get; set; }
        public bool AleCrawEnabled { get; set; }
    }
}
