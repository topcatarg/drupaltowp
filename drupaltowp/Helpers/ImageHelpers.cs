using drupaltowp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL.Models;
using Dapper;
using MySql.Data.MySqlClient;
using drupaltowp.Configuracion;

namespace drupaltowp.Helpers;

internal static class ImageHelpers
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="wpMedia">Media item de wordpress</param>
    /// <param name="originalFile">Nombre del archivo original</param>
    /// <returns></returns>
    public static string GenerateWordPressImageHtml(MediaItem wpMedia, string originalFile)
    {
        // Obtener dimensiones si están disponibles
        var width = wpMedia.MediaDetails?.Width.ToString() ?? "";
        var height = wpMedia.MediaDetails?.Height.ToString() ?? "";
       
        // Generar alt text inteligente
        var altText = !string.IsNullOrEmpty(wpMedia.AltText)
            ? wpMedia.AltText
            : !string.IsNullOrEmpty(wpMedia.Title?.Rendered)
                ? wpMedia.Title.Rendered
                : Path.GetFileNameWithoutExtension(originalFile);

        // 🎯 GENERAR HTML DE WORDPRESS ESTÁNDAR
        var imgHtml = $"<img src=\"{wpMedia.SourceUrl}\" alt=\"{altText}\"";

        if (!string.IsNullOrEmpty(width) && !string.IsNullOrEmpty(height))
        {
            imgHtml += $" width=\"{width}\" height=\"{height}\"";
        }

        imgHtml += $" class=\"wp-image-{wpMedia.Id}\" />";

        // Envolver en párrafo si no viene de un <img> tradicional
        return $"<p>{imgHtml}</p>";
    }

    public static async Task<DrupalImage> GetDrupalImageData(int DrupalFID)
    {
        using MySqlConnection conn = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
        await conn.OpenAsync();
        string Query = @"
SELECT *
from file_managed
where fid = @fid";
        return await conn.QueryFirstAsync<DrupalImage>(Query, new { fid = DrupalFID });
    }
}
