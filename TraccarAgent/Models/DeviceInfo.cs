namespace TraccarAgent.Models;

// Lo que guardamos en cache por cada dispositivo
public class DeviceInfo
{
    public string AccountId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? CalendarId { get; set; }
    public int? PositionId { get; set; }
    public long? MotionTimeMs { get; set; }
    public double? MotionLat { get; set; }
    public double? MotionLon { get; set; }
}
