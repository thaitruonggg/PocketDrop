// PocketDrop
// Copyright (C) 2026 Naofunyan
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.

using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace PocketDrop
{
    public static class ImageProcessor
    {
        // ================================================ //
        // 1. METADATA STRIPPING
        // ================================================ //

        // Strips all EXIF metadata from an image and saves a clean copy.
        // Returns the new file path, or null if the format is unsupported.
        public static string StripMetadata(string inputPath, string outputFolder)
        {
            string ext = Path.GetExtension(inputPath).ToLower();
            string filename = Path.GetFileNameWithoutExtension(inputPath);
            string cleanFileName = $"{filename}_Clean{ext}";
            string cleanFilePath = Path.Combine(outputFolder, $"PocketDrop_{Guid.NewGuid().ToString("N").Substring(0, 8)}_{cleanFileName}");

            using (var fs = File.OpenRead(inputPath))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);

                BitmapEncoder encoder = null;
                if (ext == ".png") encoder = new PngBitmapEncoder();
                else if (ext == ".jpg" || ext == ".jpeg") encoder = new JpegBitmapEncoder { QualityLevel = 100 };
                else if (ext == ".bmp") encoder = new BmpBitmapEncoder();
                else if (ext == ".gif") encoder = new GifBitmapEncoder();

                if (encoder == null) return null;

                foreach (BitmapFrame frame in decoder.Frames)
                {
                    var cleanSource = new FormatConvertedBitmap(frame, frame.Format, frame.Palette, 0);
                    encoder.Frames.Add(BitmapFrame.Create(cleanSource, null, (BitmapMetadata)null, null));
                }

                using (var outStream = new FileStream(cleanFilePath, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(outStream);
                }
            }

            return cleanFilePath;
        }


        // ================================================ //
        // 2. FORMAT CONVERSION
        // ================================================ //

        // Converts an image to a different format (PNG, JPG, WEBP, or PDF).
        // Returns the new file path, or null if skipped.
        public static string ConvertFormat(string inputPath, string targetExt, string outputFolder)
        {
            string originalExt = Path.GetExtension(inputPath).ToLower();
            if (originalExt == targetExt) return null; // Already the correct format

            string filename = Path.GetFileNameWithoutExtension(inputPath);
            string newFileName = $"{filename}{targetExt}";
            string targetFilePath = Path.Combine(outputFolder, newFileName);

            // Safe Collision Check
            targetFilePath = GetUniqueFilePath(targetFilePath);
            newFileName = Path.GetFileName(targetFilePath);

            using (var fs = File.OpenRead(inputPath))
            {
                // Route A: Native WPF for PNG / JPG
                if (targetExt == ".png" || targetExt == ".jpg")
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    BitmapEncoder encoder = targetExt == ".png" ? (BitmapEncoder)new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 100 };

                    var standardizedFrame = new FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    encoder.Frames.Add(BitmapFrame.Create(standardizedFrame));

                    using (var outStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write))
                    {
                        encoder.Save(outStream);
                    }
                }
                // Route B: SkiaSharp for WEBP
                else if (targetExt == ".webp")
                {
                    using (var originalBitmap = SkiaSharp.SKBitmap.Decode(fs))
                    using (var image = SkiaSharp.SKImage.FromBitmap(originalBitmap))
                    using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Webp, 100))
                    using (var outStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write))
                    {
                        data.SaveTo(outStream);
                    }
                }
                // Route C: PDFsharp for Individual PDFs
                else if (targetExt == ".pdf")
                {
                    using (var document = new PdfSharp.Pdf.PdfDocument())
                    {
                        var page = document.AddPage();
                        using (var xImage = PdfSharp.Drawing.XImage.FromStream(fs))
                        {
                            page.Width = xImage.PointWidth;
                            page.Height = xImage.PointHeight;

                            using (var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page))
                            {
                                gfx.DrawImage(xImage, 0, 0, page.Width, page.Height);
                            }
                        }
                        document.Save(targetFilePath);
                    }
                }
                else
                {
                    return null;
                }
            }

            return targetFilePath;
        }


        // ================================================ //
        // 3. IMAGE ROTATION
        // ================================================ //

        // Rotates an image by the specified degrees (handles EXIF orientation correction).
        // Returns the new file path, or null on failure.
        public static string RotateImage(string inputPath, int userDegrees, string rotatedSuffix, string outputFolder)
        {
            string ext = Path.GetExtension(inputPath).ToLower();
            string filename = Path.GetFileNameWithoutExtension(inputPath);
            string newFileName = $"{filename}{rotatedSuffix}{ext}";
            string targetFilePath = Path.Combine(outputFolder, newFileName);

            // Safe Collision Check
            targetFilePath = GetUniqueFilePath(targetFilePath);

            using (var fs = File.OpenRead(inputPath))
            using (var codec = SkiaSharp.SKCodec.Create(fs))
            {
                if (codec == null) return null;

                using (var rawBitmap = SkiaSharp.SKBitmap.Decode(codec))
                {
                    if (rawBitmap == null) return null;

                    // Detect embedded EXIF orientation
                    int exifDegrees = 0;
                    switch (codec.EncodedOrigin)
                    {
                        case SkiaSharp.SKEncodedOrigin.BottomRight: exifDegrees = 180; break;
                        case SkiaSharp.SKEncodedOrigin.RightTop: exifDegrees = 90; break;
                        case SkiaSharp.SKEncodedOrigin.LeftBottom: exifDegrees = 270; break;
                    }

                    int safeUserDegrees = userDegrees < 0 ? userDegrees + 360 : userDegrees;
                    int totalDegrees = (exifDegrees + safeUserDegrees) % 360;

                    SkiaSharp.SKEncodedImageFormat format = SkiaSharp.SKEncodedImageFormat.Jpeg;
                    if (ext == ".png") format = SkiaSharp.SKEncodedImageFormat.Png;
                    else if (ext == ".webp") format = SkiaSharp.SKEncodedImageFormat.Webp;
                    else if (ext == ".bmp") format = SkiaSharp.SKEncodedImageFormat.Bmp;

                    if (totalDegrees == 0)
                    {
                        using (var image = SkiaSharp.SKImage.FromBitmap(rawBitmap))
                        using (var data = image.Encode(format, 100))
                        using (var outStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write))
                        {
                            data.SaveTo(outStream);
                        }
                    }
                    else
                    {
                        int newW = (totalDegrees == 90 || totalDegrees == 270) ? rawBitmap.Height : rawBitmap.Width;
                        int newH = (totalDegrees == 90 || totalDegrees == 270) ? rawBitmap.Width : rawBitmap.Height;

                        using (var rotatedBitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(newW, newH)))
                        using (var canvas = new SkiaSharp.SKCanvas(rotatedBitmap))
                        {
                            canvas.Clear(SkiaSharp.SKColors.Transparent);
                            canvas.Translate(newW / 2f, newH / 2f);
                            canvas.RotateDegrees(totalDegrees);
                            canvas.Translate(-rawBitmap.Width / 2f, -rawBitmap.Height / 2f);
                            canvas.DrawBitmap(rawBitmap, 0, 0);

                            using (var image = SkiaSharp.SKImage.FromBitmap(rotatedBitmap))
                            using (var data = image.Encode(format, 100))
                            using (var outStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write))
                            {
                                data.SaveTo(outStream);
                            }
                        }
                    }
                }
            }

            return targetFilePath;
        }


        // ================================================ //
        // 4. IMAGE RESIZING
        // ================================================ //

        // Resizes an image using Fit, Fill, or Stretch mode with Pixels, Percentages, or Centimeters.
        // Returns the new file path, or null on failure.
        public static string ResizeImage(string inputPath, double inputW, double inputH,
            ImageResizeMode mode, ImageResizeUnit unit, string resizedSuffix, string outputFolder)
        {
            string ext = Path.GetExtension(inputPath).ToLower();
            string filename = Path.GetFileNameWithoutExtension(inputPath);
            string newFileName = $"{filename}{resizedSuffix}{ext}";
            string targetFilePath = Path.Combine(outputFolder, newFileName);

            // Safe Collision Check
            targetFilePath = GetUniqueFilePath(targetFilePath);

            double standardDpi = 96.0;

            using (var fs = File.OpenRead(inputPath))
            using (var originalBitmap = SkiaSharp.SKBitmap.Decode(fs))
            {
                if (originalBitmap == null) return null;

                int originalW = originalBitmap.Width;
                int originalH = originalBitmap.Height;

                // Step A: Convert user inputs to a Target Pixel Box
                double targetBoxW = 0;
                double targetBoxH = 0;

                if (unit == ImageResizeUnit.Pixels)
                {
                    targetBoxW = inputW;
                    targetBoxH = inputH;
                }
                else if (unit == ImageResizeUnit.Percentages)
                {
                    targetBoxW = originalW * (inputW / 100.0);
                    targetBoxH = originalH * (inputH / 100.0);
                }
                else if (unit == ImageResizeUnit.Centimeters)
                {
                    targetBoxW = (inputW / 2.54) * standardDpi;
                    targetBoxH = (inputH / 2.54) * standardDpi;
                }

                // Step B: Auto-calculate missing dimension to maintain ratio
                if (targetBoxW > 0 && targetBoxH <= 0)
                    targetBoxH = originalH * (targetBoxW / originalW);
                else if (targetBoxH > 0 && targetBoxW <= 0)
                    targetBoxW = originalW * (targetBoxH / originalH);

                // Step C: Calculate actual resize dimensions based on Mode
                int scaleW = originalW;
                int scaleH = originalH;

                if (targetBoxW > 0 && targetBoxH > 0)
                {
                    if (mode == ImageResizeMode.Stretch)
                    {
                        scaleW = (int)targetBoxW;
                        scaleH = (int)targetBoxH;
                    }
                    else
                    {
                        double ratioX = targetBoxW / originalW;
                        double ratioY = targetBoxH / originalH;
                        double ratio = (mode == ImageResizeMode.Fit)
                            ? Math.Min(ratioX, ratioY)
                            : Math.Max(ratioX, ratioY);

                        scaleW = (int)(originalW * ratio);
                        scaleH = (int)(originalH * ratio);
                    }
                }

                if (scaleW <= 0 || scaleH <= 0) return null;

                // Step D: Perform High-Quality Skia Resize
                using (var resizedBitmap = originalBitmap.Resize(new SkiaSharp.SKImageInfo(scaleW, scaleH), SkiaSharp.SKFilterQuality.High))
                using (var scaledImage = SkiaSharp.SKImage.FromBitmap(resizedBitmap))
                {
                    SkiaSharp.SKImage finalImage = scaledImage;

                    // Step E: If 'Fill', center-crop to cut off bleeding edges
                    if (mode == ImageResizeMode.Fill && inputW > 0 && inputH > 0)
                    {
                        int cropBoxW = (int)targetBoxW;
                        int cropBoxH = (int)targetBoxH;
                        int left = (scaleW - cropBoxW) / 2;
                        int top = (scaleH - cropBoxH) / 2;

                        finalImage = scaledImage.Subset(new SkiaSharp.SKRectI(left, top, left + cropBoxW, top + cropBoxH));
                    }

                    // Step F: Encode to match original format
                    SkiaSharp.SKEncodedImageFormat format = SkiaSharp.SKEncodedImageFormat.Jpeg;
                    if (ext == ".png") format = SkiaSharp.SKEncodedImageFormat.Png;
                    else if (ext == ".webp") format = SkiaSharp.SKEncodedImageFormat.Webp;
                    else if (ext == ".bmp") format = SkiaSharp.SKEncodedImageFormat.Bmp;

                    using (var data = finalImage.Encode(format, 95))
                    using (var outStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write))
                    {
                        data.SaveTo(outStream);
                    }

                    if (finalImage != scaledImage) finalImage.Dispose();
                }
            }

            return targetFilePath;
        }


        // ================================================ //
        // 5. PDF CREATION
        // ================================================ //

        // Merges a list of images into a single multi-page PDF document.
        // Returns the output file path on success.
        public static string CreatePdfFromImages(List<string> imagePaths, string outputPath)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using (var document = new PdfSharp.Pdf.PdfDocument())
            {
                foreach (var path in imagePaths)
                {
                    var page = document.AddPage();

                    using (var fs = File.OpenRead(path))
                    using (var xImage = PdfSharp.Drawing.XImage.FromStream(fs))
                    {
                        page.Width = xImage.PointWidth;
                        page.Height = xImage.PointHeight;

                        using (var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page))
                        {
                            gfx.DrawImage(xImage, 0, 0, page.Width, page.Height);
                        }
                    }
                }

                document.Save(outputPath);
            }

            return outputPath;
        }


        // ================================================ //
        // 6. INTERNAL HELPERS
        // ================================================ //

        // Generates a unique file path by appending (1), (2), etc. if the file already exists.
        private static string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath)) return filePath;

            string folder = Path.GetDirectoryName(filePath);
            string nameOnly = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);

            int counter = 1;
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(folder, $"{nameOnly} ({counter}){extension}");
                counter++;
            }

            return filePath;
        }
    }
}
