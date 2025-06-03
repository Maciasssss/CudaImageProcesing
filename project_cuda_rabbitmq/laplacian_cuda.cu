#include <cuda_runtime.h>
#include <stdio.h>

// Standardowy kernel Laplace'a 5x5
__constant__ int laplacian_kernel_5x5[25] = {
    0, 0, -1, 0, 0,
    0, -1, -2, -1, 0,
    -1, -2, 16, -2, -1,
    0, -1, -2, -1, 0,
    0, 0, -1, 0, 0};

// Funkcja pomocnicza do pobierania wartości piksela z obsługą brzegów (clamp to edge)
__device__ unsigned char get_pixel_value(const unsigned char *image, int x, int y, int width, int height, int channel, int channels)
{
    x = max(0, min(x, width - 1));
    y = max(0, min(y, height - 1));
    return image[(y * width + x) * channels + channel];
}

__global__ void apply_laplacian_kernel(
    const unsigned char *input_image,
    unsigned char *output_image,
    int width,
    int height,
    int channels)
{
    int x = blockIdx.x * blockDim.x + threadIdx.x;
    int y = blockIdx.y * blockDim.y + threadIdx.y;

    if (x < width && y < height)
    {
        for (int c = 0; c < channels; ++c)
        {
            float sum = 0.0f;
            int kernel_idx = 0;

            for (int ky = -2; ky <= 2; ++ky)
            {
                for (int kx = -2; kx <= 2; ++kx)
                {
                    sum += (float)get_pixel_value(input_image, x + kx, y + ky, width, height, c, channels) *
                           laplacian_kernel_5x5[kernel_idx++];
                }
            }

            // Normalizacja/przycięcie wyniku do zakresu 0-255
            sum = fmaxf(0.0f, fminf(255.0f, sum));
            output_image[(y * width + x) * channels + c] = (unsigned char)sum;
        }
    }
}

extern "C" __declspec(dllexport) void
process_image_cuda(
    const unsigned char *h_input_image,
    unsigned char *h_output_image,
    int width,
    int height,
    int channels,
    float *elapsed_ms)
{
    unsigned char *d_input_image, *d_output_image;
    size_t image_size_bytes = width * height * channels * sizeof(unsigned char);

    cudaEvent_t start, stop;
    cudaEventCreate(&start);
    cudaEventCreate(&stop);

    // Alokacja pamięci na GPU
    cudaMalloc((void **)&d_input_image, image_size_bytes);
    cudaMalloc((void **)&d_output_image, image_size_bytes);

    // Kopiowanie danych z hosta do urządzenia
    cudaMemcpy(d_input_image, h_input_image, image_size_bytes, cudaMemcpyHostToDevice);

    // Konfiguracja siatki i bloków
    dim3 threads_per_block(16, 16);
    dim3 num_blocks((width + threads_per_block.x - 1) / threads_per_block.x,
                    (height + threads_per_block.y - 1) / threads_per_block.y);

    // Rozpocznij pomiar czasu
    cudaEventRecord(start);

    // Wywołanie kernela
    apply_laplacian_kernel<<<num_blocks, threads_per_block>>>(
        d_input_image, d_output_image, width, height, channels);
    cudaDeviceSynchronize();

    // Zatrzymaj pomiar czasu
    cudaEventRecord(stop);
    cudaEventSynchronize(stop);
    cudaEventElapsedTime(elapsed_ms, start, stop);

    // Kopiowanie wyników z urządzenia do hosta
    cudaMemcpy(h_output_image, d_output_image, image_size_bytes, cudaMemcpyDeviceToHost);

    // Zwolnienie pamięci na GPU
    cudaFree(d_input_image);
    cudaFree(d_output_image);
    cudaEventDestroy(start);
    cudaEventDestroy(stop);
}