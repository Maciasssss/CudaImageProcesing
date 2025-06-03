using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using CudaImageProcessing.Shared;

namespace ImageWorkerCs
{
    class Worker
    {
        private const string RABBITMQ_HOST = "localhost";
        private const string TASK_QUEUE_NAME = "task_queue_laplacian";
        private const string CUDA_DLL_PATH = "laplacian_processor.dll";

        [DllImport(CUDA_DLL_PATH, CallingConvention = CallingConvention.Cdecl)]
        public static extern void process_image_cuda(
            byte[] inputImage,
            byte[] outputImage,
            int width,
            int height,
            int channels,
            ref float elapsedMs);

        public static void Main(string[] args)
        {
            var factory = new ConnectionFactory() { HostName = RABBITMQ_HOST };

            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                channel.QueueDeclare(queue: TASK_QUEUE_NAME,
                                     durable: true,
                                     exclusive: false,
                                     autoDelete: false,
                                     arguments: null);
                channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

                Console.WriteLine(" [*] Worker: Waiting for messages. To exit press CTRL+C");

                var consumer = new EventingBasicConsumer(channel);
                consumer.Received += (model, ea) =>
                {
                    var body = ea.Body.ToArray();
                    var messageJson = Encoding.UTF8.GetString(body);
                    var props = ea.BasicProperties;
                    var replyProps = channel.CreateBasicProperties();
                    replyProps.CorrelationId = props.CorrelationId;

                    ImageProcessingResponse? response = null;
                    Stopwatch workerStopwatch = Stopwatch.StartNew();

                    try
                    {
                        var request = JsonSerializer.Deserialize<ImageProcessingRequest>(messageJson);

                        if (request == null)
                        {
                            Console.WriteLine($" [!] Worker: Failed to deserialize request for CorrelationId {props.CorrelationId}. Message: {messageJson}");
                            response = new ImageProcessingResponse { GpuProcessingTimeMs = -2, WorkerTotalTimeMs = workerStopwatch.Elapsed.TotalMilliseconds };
                        }
                        else if (string.IsNullOrEmpty(request.ImageDataB64))
                        {
                            Console.WriteLine($" [!] Worker: ImageDataB64 is null or empty for CorrelationId {props.CorrelationId}");
                            response = new ImageProcessingResponse { GpuProcessingTimeMs = -3, Width = request.Width, Height = request.Height, Channels = request.Channels, WorkerTotalTimeMs = workerStopwatch.Elapsed.TotalMilliseconds }; // Brak danych obrazu
                        }
                        else
                        {
                            Console.WriteLine($" [*] Worker: Received task with CorrelationId {props.CorrelationId} for image {request.Width}x{request.Height}x{request.Channels}");

                            byte[] inputImageData = Convert.FromBase64String(request.ImageDataB64);
                            byte[] outputImageData = new byte[inputImageData.Length];
                            float gpuTimeMs = 0;

                            Stopwatch cudaCallStopwatch = Stopwatch.StartNew();
                            process_image_cuda(inputImageData, outputImageData, request.Width, request.Height, request.Channels, ref gpuTimeMs);
                            cudaCallStopwatch.Stop();
                            Console.WriteLine($" [*] Worker: CUDA function call (including marshalling) took: {cudaCallStopwatch.Elapsed.TotalMilliseconds:F2} ms. Reported GPU time: {gpuTimeMs:F2} ms");

                            response = new ImageProcessingResponse
                            {
                                ProcessedImageDataB64 = Convert.ToBase64String(outputImageData),
                                Width = request.Width,
                                Height = request.Height,
                                Channels = request.Channels,
                                GpuProcessingTimeMs = gpuTimeMs,
                            };
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        Console.WriteLine($" [!] Worker: JSON Deserialization error for CorrelationId {props.CorrelationId}: {jsonEx.Message}. Original message: {messageJson}");
                        response = new ImageProcessingResponse { GpuProcessingTimeMs = -4, WorkerTotalTimeMs = workerStopwatch.Elapsed.TotalMilliseconds };
                    }
                    catch (FormatException formatEx)
                    {
                        Console.WriteLine($" [!] Worker: Base64 Format error for CorrelationId {props.CorrelationId}: {formatEx.Message}");
                        response = new ImageProcessingResponse { GpuProcessingTimeMs = -5, WorkerTotalTimeMs = workerStopwatch.Elapsed.TotalMilliseconds };
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($" [!] Worker: General error processing message for CorrelationId {props.CorrelationId}: {e}");
                        response = new ImageProcessingResponse { GpuProcessingTimeMs = -1, WorkerTotalTimeMs = workerStopwatch.Elapsed.TotalMilliseconds };
                    }
                    finally
                    {
                        workerStopwatch.Stop();
                        if (response != null)
                        {
                            if (response.WorkerTotalTimeMs == 0)
                            {
                                response.WorkerTotalTimeMs = workerStopwatch.Elapsed.TotalMilliseconds;
                            }

                            var responseJson = JsonSerializer.Serialize(response);
                            var responseBytes = Encoding.UTF8.GetBytes(responseJson);

                            try
                            {
                                channel.BasicPublish(exchange: "",
                                                     routingKey: props.ReplyTo,
                                                     basicProperties: replyProps,
                                                     body: responseBytes);
                                Console.WriteLine($" [*] Worker: Sent response for CorrelationId {props.CorrelationId}. Total worker time: {response.WorkerTotalTimeMs:F2} ms.");
                            }
                            catch (Exception pubEx)
                            {
                                Console.WriteLine($" [!] Worker: Error publishing response for CorrelationId {props.CorrelationId}: {pubEx.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($" [!] Worker: Response object was null for CorrelationId {props.CorrelationId}. This should not happen.");
                        }
                        channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                };
                channel.BasicConsume(queue: TASK_QUEUE_NAME,
                                     autoAck: false,
                                     consumer: consumer);

                Console.WriteLine(" Press [enter] to exit.");
                Console.ReadLine();
            }
        }
    }
}