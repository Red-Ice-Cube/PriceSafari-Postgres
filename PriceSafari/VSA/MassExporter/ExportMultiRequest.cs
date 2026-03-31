namespace PriceSafari.VSA.MassExporter
{
    public class ExportMultiRequest
    {
        public List<int> ScrapIds { get; set; }
        public string ConnectionId { get; set; }
        public string ExportType { get; set; }

        /// <summary>
        /// "comparison" = Ceneo/Google (ScrapHistories + PriceHistories)
        /// "marketplace" = Allegro (AllegroScrapeHistories + AllegroPriceHistories)
        /// </summary>
        public string SourceType { get; set; } = "comparison";
    }
}