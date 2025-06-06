using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace drupaltowp.Services;

    internal class SimpleImageMetadataExtractor
    {

    public class BasicImageMetadata
    {
        public int Width { get; set; } = 0;
        public int Height { get; set; } = 0;
        public string MimeType { get; set; }
        public long FileSize { get; set; }
        public string FileName { get; set; }
    }
    /// <summary>
    /// Extrae solo info del archivo (súper rápido)
    /// WordPress calculará dimensiones después si es necesario
    /// </summary>
    public static BasicImageMetadata ExtractBasicMetadata(string imagePath)
    {
        var fileInfo = new FileInfo(imagePath);

        return new BasicImageMetadata
        {
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
            MimeType = GetMimeTypeFromExtension(fileInfo.Extension),
            Width = 0,    // WordPress lo calculará después
            Height = 0    // WordPress lo calculará después
        };
    }

    /// <summary>
    /// MIME type basado en extensión (muy rápido)
    /// </summary>
    public static string GetMimeTypeFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "image/jpeg" // Fallback seguro
        };
    }

    public static string CreateSimpleWordPressMetadata(string filePath, BasicImageMetadata metadata)
    {
        var relativePath = filePath.Replace('\\', '/');

        // Metadata mínimo - WordPress calculará dimensiones si las necesita
        return $"a:4:{{s:5:\"width\";i:{metadata.Width};s:6:\"height\";i:{metadata.Height};s:4:\"file\";s:{relativePath.Length}:\"{relativePath}\";s:5:\"sizes\";a:0:{{}}}}";
    }

    public static string CreateSimpleAltText(string filename)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        return nameWithoutExt.Replace("-", " ").Replace("_", " ");
    }

}

