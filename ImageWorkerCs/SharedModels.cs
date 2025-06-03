namespace CudaImageProcessing.Shared
{
    public class ImageProcessingRequest
    {
        public string? ImageDataB64 { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Channels { get; set; }
    }

    public class ImageProcessingResponse
    {
        public string? ProcessedImageDataB64 { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Channels { get; set; }
        public float GpuProcessingTimeMs { get; set; }
        public double WorkerTotalTimeMs { get; set; }
    }
}