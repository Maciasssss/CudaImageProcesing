using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageClientCs
{
    public static class ImageUtils
    {
        // Konwertuje Bitmap na surowe dane pikseli (BGR, 3 kanały)
        public static byte[] GetPixelDataFromBitmap(Bitmap bmp, out int width, out int height, out int channels)
        {
            width = bmp.Width;
            height = bmp.Height;
            channels = 3; // Zakładamy, że obraz jest w formacie 24 bity na piksel (3 kanały BGR)
            Bitmap bmpToProcess = bmp;
            // Upewnij się, że bitmapa jest w formacie Format24bppRgb
            if (bmp.PixelFormat != PixelFormat.Format24bppRgb)
            {
                bmpToProcess = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(bmpToProcess))
                {
                    g.DrawImage(bmp, 0, 0, width, height);
                }
                Console.WriteLine($"Converted image from {bmp.PixelFormat} to {PixelFormat.Format24bppRgb}");
            }

            BitmapData bmpData = bmpToProcess.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            byte[] pixelData = new byte[width * height * channels];

            // Kopiuj dane, uwzględniając stride (szerokość wiersza w pamięci)
            if (Math.Abs(bmpData.Stride) == width * channels)
            {
                // Jeśli nie ma paddingu, kopiuj bezpośrednio
                Marshal.Copy(bmpData.Scan0, pixelData, 0, pixelData.Length);
            }
            else
            {
                // Kopiuj wiersz po wierszu, aby pominąć padding
                for (int i = 0; i < height; ++i)
                {
                    IntPtr Ptr = IntPtr.Add(bmpData.Scan0, i * bmpData.Stride);
                    Marshal.Copy(Ptr, pixelData, i * width * channels, width * channels);
                }
            }

            bmpToProcess.UnlockBits(bmpData);
            if (bmpToProcess != bmp)
            {
                bmpToProcess.Dispose();
            }
            return pixelData;
        }

        // Tworzy Bitmap z surowych danych pikseli (oczekuje BGR, 3 kanały)
        public static Bitmap CreateBitmapFromPixelData(byte[] pixelData, int width, int height, int channels)
        {
            if (channels != 3)
            {
                throw new ArgumentException("This helper only supports 3 channels (Format24bppRgb).");
            }

            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            BitmapData bmpData = bmp.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);

            if (Math.Abs(bmpData.Stride) == width * channels)
            {
                Marshal.Copy(pixelData, 0, bmpData.Scan0, pixelData.Length);
            }
            else
            {
                for (int i = 0; i < height; ++i)
                {
                    IntPtr Ptr = IntPtr.Add(bmpData.Scan0, i * bmpData.Stride);
                    Marshal.Copy(pixelData, i * width * channels, Ptr, width * channels);
                }
            }

            bmp.UnlockBits(bmpData);
            return bmp;
        }
    }
}