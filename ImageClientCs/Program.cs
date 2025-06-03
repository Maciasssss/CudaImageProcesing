using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CudaImageProcessing.Shared;

namespace ImageClientCs
{
    public class RpcClient : IDisposable
    {
        private const string RABBITMQ_HOST = "localhost";
        private const string TASK_QUEUE_NAME = "task_queue_laplacian";

        private readonly IConnection _connection;
        private readonly IModel _channel;
        private readonly string _replyQueueName;
        private readonly EventingBasicConsumer _consumer;
        private readonly BlockingCollection<ImageProcessingResponse> _responseQueue = new BlockingCollection<ImageProcessingResponse>();
        private string? _currentCorrelationId;
        public double LastOverallTimeMs { get; private set; }
        public double LastCommunicationOverheadMs { get; private set; }
        public RpcClient()
        {
            var factory = new ConnectionFactory() { HostName = RABBITMQ_HOST };
            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _replyQueueName = _channel.QueueDeclare(autoDelete: true).QueueName;
            _consumer = new EventingBasicConsumer(_channel);
            _consumer.Received += (model, ea) =>
            {
                if (ea.BasicProperties.CorrelationId == _currentCorrelationId)
                {
                    var body = ea.Body.ToArray();
                    var responseJson = Encoding.UTF8.GetString(body);
                    var response = JsonSerializer.Deserialize<ImageProcessingResponse>(responseJson);
                    if (response != null)
                    {
                        _responseQueue.Add(response);
                        Console.WriteLine(" [.] Client: Received response.");
                    }
                }
                else
                {
                    Console.WriteLine($" [.] Client: Received message with mismatched CorrelationId. Expected: {_currentCorrelationId}, Got: {ea.BasicProperties.CorrelationId}");
                }
            };

            _channel.BasicConsume(
                consumer: _consumer,
                queue: _replyQueueName,
                autoAck: true);
        }

        public ImageProcessingResponse? Call(string imagePath, string outputPath = "processed_image_cs.png")
        {
            Bitmap originalBitmap;
            Stopwatch overallStopwatch = Stopwatch.StartNew();

            try
            {
                originalBitmap = new Bitmap(imagePath);
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($" [!] Client: Image file not found at {imagePath}");
                overallStopwatch.Stop();
                LastOverallTimeMs = overallStopwatch.Elapsed.TotalMilliseconds;
                LastCommunicationOverheadMs = -1;
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [!] Client: Error loading image {imagePath}: {ex.Message}");
                overallStopwatch.Stop();
                LastOverallTimeMs = overallStopwatch.Elapsed.TotalMilliseconds;
                LastCommunicationOverheadMs = -1;
                return null;
            }

            byte[] pixelData = ImageUtils.GetPixelDataFromBitmap(originalBitmap, out int width, out int height, out int channels);
            originalBitmap.Dispose();

            var request = new ImageProcessingRequest
            {
                ImageDataB64 = Convert.ToBase64String(pixelData),
                Width = width,
                Height = height,
                Channels = channels
            };

            _currentCorrelationId = Guid.NewGuid().ToString();
            var props = _channel.CreateBasicProperties();
            props.CorrelationId = _currentCorrelationId;
            props.ReplyTo = _replyQueueName;
            props.Persistent = true;

            var messageBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

            Console.WriteLine($" [x] Client: Sending request for {imagePath} with CorrelationId {_currentCorrelationId} ({width}x{height}x{channels})");

            // Stopwatch dla samego czasu oczekiwania na odpowiedź (komunikacja + przetwarzanie workera)
            Stopwatch communicationStopwatch = new Stopwatch();

            _channel.BasicPublish(
                exchange: "",
                routingKey: TASK_QUEUE_NAME,
                basicProperties: props,
                body: messageBytes);

            communicationStopwatch.Start();
            Console.WriteLine(" [.] Client: Waiting for response...");

            ImageProcessingResponse? response = null;
            if (_responseQueue.TryTake(out response, TimeSpan.FromSeconds(60)))
            {
                communicationStopwatch.Stop();

                Console.WriteLine(" [.] Client: Processing response data...");
                if (response.GpuProcessingTimeMs < 0)
                {
                    Console.WriteLine(" [!] Client: Worker reported an error.");
                    overallStopwatch.Stop();
                    LastOverallTimeMs = overallStopwatch.Elapsed.TotalMilliseconds;
                    LastCommunicationOverheadMs = communicationStopwatch.Elapsed.TotalMilliseconds;
                    return response;
                }

                byte[] processedPixelData = Convert.FromBase64String(response.ProcessedImageDataB64!);
                Bitmap processedBitmap = ImageUtils.CreateBitmapFromPixelData(processedPixelData, response.Width, response.Height, response.Channels);

                try
                {
                    processedBitmap.Save(outputPath, ImageFormat.Png);
                    Console.WriteLine($" [.] Client: Processed image saved to {outputPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" [!] Client: Error saving image {outputPath}: {ex.Message}");
                }
                processedBitmap.Dispose();

                // Wszystkie operacje klienta dla tego żądania zakończone
                overallStopwatch.Stop();
                LastOverallTimeMs = overallStopwatch.Elapsed.TotalMilliseconds;
                LastCommunicationOverheadMs = communicationStopwatch.Elapsed.TotalMilliseconds;

            }
            else
            {
                communicationStopwatch.Stop();
                overallStopwatch.Stop();

                LastOverallTimeMs = overallStopwatch.Elapsed.TotalMilliseconds;
                LastCommunicationOverheadMs = communicationStopwatch.Elapsed.TotalMilliseconds;
                Console.WriteLine(" [!] Client: Timeout waiting for response.");
            }
            return response;
        }

        public void Dispose()
        {
            _channel.Close();
            _connection.Close();
            _channel.Dispose();
            _connection.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("C# Image Client - Performance Test for Laplacian Edge Detection");

            // Lista obrazów do przetestowania (ścieżka, opis)
            var testScenarios = new List<(string ImagePath, string Description, string OutputPath)>
        {
            ("test_image_small_cs.png", "Small Image (320x240)", "processed_small_cs.png"),
            ("test_image_medium_cs.png", "Medium Image (640x480)", "processed_medium_cs.png"),
            ("test_image_large_cs.png", "Large Image (1920x1080)", "processed_large_cs.png"),
            // Można dodać tutaj ścieżkę do innego "image.png"
        };

            // Utwórz obrazy testowe, jeśli nie istnieją
            CreateTestImageIfNotExists("test_image_small_cs.png", 320, 240, Color.LightBlue);
            CreateTestImageIfNotExists("test_image_medium_cs.png", 640, 480, Color.LightCoral);
            CreateTestImageIfNotExists("test_image_large_cs.png", 1920, 1080, Color.LightGreen);


            int numberOfRunsPerScenario = 5; // Ile razy przetwarzać każdy obraz
            var allResults = new List<ScenarioResult>();

            using (var client = new RpcClient())
            {
                foreach (var scenario in testScenarios)
                {
                    if (!File.Exists(scenario.ImagePath))
                    {
                        Console.WriteLine($"\n[!] Scenario '{scenario.Description}' skipped: Image file not found at {Path.GetFullPath(scenario.ImagePath)}");
                        continue;
                    }

                    Console.WriteLine($"\n--- Processing Scenario: {scenario.Description} ({scenario.ImagePath}) ---");
                    var scenarioTimings = new ScenarioResult { Description = scenario.Description };

                    for (int i = 0; i < numberOfRunsPerScenario; i++)
                    {
                        Console.WriteLine($"  Run {i + 1}/{numberOfRunsPerScenario}...");
                        var response = client.Call(scenario.ImagePath, $"{Path.GetFileNameWithoutExtension(scenario.OutputPath)}_run{i + 1}{Path.GetExtension(scenario.OutputPath)}");

                        if (response != null && response.GpuProcessingTimeMs >= 0)
                        {
                            scenarioTimings.ClientOverallTimes.Add(client.LastOverallTimeMs);
                            scenarioTimings.CommunicationOverheadTimes.Add(client.LastCommunicationOverheadMs);
                            scenarioTimings.WorkerTotalTimes.Add(response.WorkerTotalTimeMs);
                            scenarioTimings.GpuKernelTimes.Add(response.GpuProcessingTimeMs);
                            scenarioTimings.WorkerCpuOverheadTimes.Add(response.WorkerTotalTimeMs - response.GpuProcessingTimeMs);
                        }
                        else
                        {
                            Console.WriteLine("    Run failed or worker reported an error.");
                        }
                    }
                    allResults.Add(scenarioTimings);
                }
            }

            Console.WriteLine("\n\n--- Aggregate Performance Report ---");
            foreach (var result in allResults)
            {
                if (result.ClientOverallTimes.Any())
                {
                    Console.WriteLine($"\nScenario: {result.Description}");
                    Console.WriteLine($"  Avg Client Overall Time:        {result.ClientOverallTimes.Average():F2} ms (Min: {result.ClientOverallTimes.Min():F2}, Max: {result.ClientOverallTimes.Max():F2})");
                    Console.WriteLine($"  Avg Communication Overhead:     {result.CommunicationOverheadTimes.Average():F2} ms (Min: {result.CommunicationOverheadTimes.Min():F2}, Max: {result.CommunicationOverheadTimes.Max():F2})");
                    Console.WriteLine($"  Avg Worker Total Time:          {result.WorkerTotalTimes.Average():F2} ms (Min: {result.WorkerTotalTimes.Min():F2}, Max: {result.WorkerTotalTimes.Max():F2})");
                    Console.WriteLine($"  Avg GPU Kernel Time:            {result.GpuKernelTimes.Average():F2} ms (Min: {result.GpuKernelTimes.Min():F2}, Max: {result.GpuKernelTimes.Max():F2})");
                    Console.WriteLine($"  Avg Worker CPU Overhead:        {result.WorkerCpuOverheadTimes.Average():F2} ms (Min: {result.WorkerCpuOverheadTimes.Min():F2}, Max: {result.WorkerCpuOverheadTimes.Max():F2})");

                    // Procentowy udział czasu GPU w całkowitym czasie workera
                    if (result.WorkerTotalTimes.Average() > 0)
                    {
                        double gpuPercentageOfWorker = (result.GpuKernelTimes.Average() / result.WorkerTotalTimes.Average()) * 100;
                        Console.WriteLine($"  GPU Kernel Time as % of Worker Total: {gpuPercentageOfWorker:F2}%");
                    }
                    // Procentowy udział czasu GPU w całkowitym czasie klienta
                    if (result.ClientOverallTimes.Average() > 0)
                    {
                        double gpuPercentageOfClient = (result.GpuKernelTimes.Average() / result.ClientOverallTimes.Average()) * 100;
                        Console.WriteLine($"  GPU Kernel Time as % of Client Overall: {gpuPercentageOfClient:F2}%");
                    }
                }
                else
                {
                    Console.WriteLine($"\nScenario: {result.Description} - No successful runs recorded.");
                }
            }
            string csvFilePath = "performance_results.csv";
            try
            {
                using (StreamWriter sw = new StreamWriter(csvFilePath))
                {
                    // Nagłówek CSV
                    sw.WriteLine("ScenarioDescription,AvgClientOverallTimeMs,MinClientOverallTimeMs,MaxClientOverallTimeMs," +
                                 "AvgCommunicationOverheadMs,MinCommunicationOverheadMs,MaxCommunicationOverheadMs," +
                                 "AvgWorkerTotalTimeMs,MinWorkerTotalTimeMs,MaxWorkerTotalTimeMs," +
                                 "AvgGpuKernelTimeMs,MinGpuKernelTimeMs,MaxGpuKernelTimeMs," +
                                 "AvgWorkerCpuOverheadMs,MinWorkerCpuOverheadMs,MaxWorkerCpuOverheadMs," +
                                 "GpuTimeAsPercentageOfWorker,GpuTimeAsPercentageOfClient");

                    foreach (var result in allResults)
                    {
                        if (result.ClientOverallTimes.Any())
                        {
                            double avgClientOverall = result.ClientOverallTimes.Average();
                            double avgCommOverhead = result.CommunicationOverheadTimes.Average();
                            double avgWorkerTotal = result.WorkerTotalTimes.Average();
                            double avgGpuKernel = result.GpuKernelTimes.Average();
                            double avgWorkerCpu = result.WorkerCpuOverheadTimes.Average();

                            double gpuPercentageOfWorker = (avgWorkerTotal > 0) ? (avgGpuKernel / avgWorkerTotal) * 100 : 0;
                            double gpuPercentageOfClient = (avgClientOverall > 0) ? (avgGpuKernel / avgClientOverall) * 100 : 0;

                            sw.WriteLine($"{result.Description.Replace(",", ";")},{avgClientOverall:F2},{result.ClientOverallTimes.Min():F2},{result.ClientOverallTimes.Max():F2}," +
                                         $"{avgCommOverhead:F2},{result.CommunicationOverheadTimes.Min():F2},{result.CommunicationOverheadTimes.Max():F2}," +
                                         $"{avgWorkerTotal:F2},{result.WorkerTotalTimes.Min():F2},{result.WorkerTotalTimes.Max():F2}," +
                                         $"{avgGpuKernel:F2},{result.GpuKernelTimes.Min():F2},{result.GpuKernelTimes.Max():F2}," +
                                         $"{avgWorkerCpu:F2},{result.WorkerCpuOverheadTimes.Min():F2},{result.WorkerCpuOverheadTimes.Max():F2}," +
                                         $"{gpuPercentageOfWorker:F2},{gpuPercentageOfClient:F2}");
                        }
                    }
                }
                Console.WriteLine($"\nResults saved to: {Path.GetFullPath(csvFilePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError saving results to CSV: {ex.Message}");
            }
            // --- KONIEC ZAPISU DO PLIKU CSV ---

            Console.WriteLine("\nClient finished. Press [enter] to exit.");
            Console.ReadLine();
        }

        static void CreateTestImageIfNotExists(string imagePath, int width, int height, Color color)
        {
            if (!File.Exists(imagePath))
            {
                try
                {
                    using (var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(color);
                        bmp.Save(imagePath, ImageFormat.Png);
                        Console.WriteLine($"Created a sample '{imagePath}' ({width}x{height} {color.Name}).");
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Could not create {imagePath}: {ex.Message}. Ensure System.Drawing.Common dependencies are met."); }
            }
        }
    }

    // Klasa pomocnicza do przechowywania wyników dla scenariusza
    public class ScenarioResult
    {
        public string Description { get; set; } = string.Empty;
        public List<double> ClientOverallTimes { get; } = new List<double>();
        public List<double> CommunicationOverheadTimes { get; } = new List<double>();
        public List<double> WorkerTotalTimes { get; } = new List<double>();
        public List<double> GpuKernelTimes { get; } = new List<double>();
        public List<double> WorkerCpuOverheadTimes { get; } = new List<double>();
    }
}