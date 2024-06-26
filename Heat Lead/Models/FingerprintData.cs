using System.ComponentModel.DataAnnotations;

public class FingerprintData
{
    [Key]
    public int FingerprintDataId { get; set; }
    public string HLTT { get; set; }
    public string HeatLeadTrackingCode { get; set; }
    public string CanvasFingerprint { get; set; }
    public string WebGLFingerprint { get; set; }
    public string WebRTCFingerprint { get; set; }
    public string BrowserLanguage { get; set; }
    public string TimeZone { get; set; }
    public string Platform { get; set; }
    public string UserAgent { get; set; }
    public string BrowserPlugins { get; set; }
    public string DetectedFonts { get; set; }
    public string CameraInfo { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public int PixelCount { get; set; }
    public float DevicePixelRatio { get; set; }
    public int ColorDepth { get; set; }
    public int LogicalProcessors { get; set; }
    public int DeviceMemory { get; set; }
    public int MaxTouchPoints { get; set; }
    public bool TouchSupport { get; set; }
    public bool HasSessionStorage { get; set; }
    public bool HasLocalStorage { get; set; }
    public bool HasIndexedDB { get; set; }
    public bool HasAddBehavior { get; set; }
    public bool HasOpenDatabase { get; set; }
    public DateTime CaptureTime { get; set; }
}