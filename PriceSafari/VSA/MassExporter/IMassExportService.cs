using System.Threading.Tasks;

namespace PriceSafari.VSA.MassExporter
{
    public interface IMassExportService
    {
        Task<(byte[] FileContent, string FileName, string ContentType)> GenerateExportAsync(
            int storeId,
            ExportMultiRequest request,
            string userId);

        Task<object> GetAvailableScrapsAsync(int storeId, string userId, string sourceType = "comparison");
    }
}