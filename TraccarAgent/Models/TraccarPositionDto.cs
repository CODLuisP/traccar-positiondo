namespace TraccarAgent.Models;

// Clase principal - representa una posición GPS enviada por Traccar
public class TraccarPositionDto
{
    public int? Id { get; set; }
    public int? DeviceId { get; set; }
    public string? Protocol { get; set; }
    public DateTime? ServerTime { get; set; }
    public DateTime? DeviceTime { get; set; }
    public DateTime? FixTime { get; set; }
    public bool? Valid { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Altitude { get; set; }
    public double? Speed { get; set; }
    public double? Course { get; set; }
    public string? Address { get; set; }
    public double? Accuracy { get; set; }
    public List<int>? GeofenceIds { get; set; }
    public Dictionary<string, object>? Attributes { get; set; }
    public TraccarNetworkDto? Network { get; set; }
}

public class TraccarNetworkDto
{
    public string? RadioType { get; set; }
    public bool? ConsiderIp { get; set; }
    public List<TraccarCellTowerDto>? CellTowers { get; set; }
}

public class TraccarCellTowerDto
{
    public int? CellId { get; set; }
    public int? LocationAreaCode { get; set; }
    public int? MobileCountryCode { get; set; }
    public int? MobileNetworkCode { get; set; }
}

public class TraccarWebhookDto
{
    public TraccarPositionDto? Position { get; set; }
    public TraccarDeviceDto? Device { get; set; }
}

public class TraccarDeviceDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? UniqueId { get; set; }
    public string? Status { get; set; }
    public int? CalendarId { get; set; }
    public int? PositionId { get; set; }
    public Dictionary<string, object>? Attributes { get; set; }
}