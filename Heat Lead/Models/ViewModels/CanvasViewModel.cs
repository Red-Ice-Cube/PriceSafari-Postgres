namespace Heat_Lead.Models.ViewModels
{
    public class CanvasViewModel
    {
        public int AffiliateLinkId { get; set; }
        public int Id { get; set; }
        public string ScriptLink { get; set; }
        public List<CanvasJSStyle> AvailableStyles { get; set; }
    }
}