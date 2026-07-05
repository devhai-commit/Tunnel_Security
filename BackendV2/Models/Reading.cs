namespace BackendV2.Models
{
    public class Reading
    {
        public int Id { get; set; }
        public string SensorId { get; set; }
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
        public string Description { get; set; }
    }
}