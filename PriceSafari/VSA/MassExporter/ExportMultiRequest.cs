namespace PriceSafari.VSA.MassExporter
{
    public class ExportMultiRequest
    {
        public List<int> ScrapIds { get; set; }
        public string ConnectionId { get; set; }
        public string ExportType { get; set; }

   
        public string SourceType { get; set; } = "comparison";
    }
}