using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Models;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Models;

namespace drupaltowp.Clases.Imagenes.Panopoly;

internal class OtherImagesMigrator(LoggerViewModel _logger, List<MigratedPostWithImage> PostList, MappingService _mappingService, WordPressClient _wpClient, CancellationToken cancellationToken)
{

    public async Task ImageProcessorAsync()
    {
        using MySqlConnection wpConnection = new(ConfiguracionGeneral.WPconnectionString);
        await wpConnection.OpenAsync();
        _logger.LogInfo("Iniciando migración de imágenes de contenido...");
        _logger.LogInfo($"Total de posts a procesar: {PostList.Count}");
        int count = 0;
        int total = PostList.Count;
        foreach (var post in PostList)
        {
            count++;
            if (count % 100 == 0)
            {
                var percentage = (count * 100.0) / total;
                _logger.LogInfo($"Procesando {count} de {total} ({percentage:F1}%)");
            }
            if (post.FeaturedImage != null)
            {
                if (post.Files.Count == 1 && post.Files[0].Fid == post.FeaturedImage.Fid)
                {
                    // Si solo hay una imagen y es la destacada, no hay imágenes de contenido
                    //_logger.LogInfo($"   📝 Post {post.DrupalPostId} ya tiene imagen destacada, no hay imágenes de contenido.");
                    continue;
                }
            }
            if (post.ContentImages.Count == 0)
            {
                continue;
            }
            _logger.LogProcess($"Procesando post {post.DrupalPostId} - {post.PostTitle}");
            
            var currentContent = await wpConnection.QueryFirstOrDefaultAsync<string>(
           "SELECT post_content FROM wp_posts WHERE ID = @postId",
           new { postId = post.WpPostId });
            foreach (var image in post.ContentImages)
            {
                //Busco el nombre del archivo en el contenido.
                List<string> Cadenas = ObtenerCadenas(image.Filename, currentContent!);
                if (Cadenas.Count > 0)
                {
                    _logger.LogInfo($"   🖼️ Encontrada imagen en contenido: {image.Filename} (FID: {image.Fid})");
                }
                foreach (var cadena in Cadenas)
                {
                    _logger.LogInfo($"   🖼️ Cadena encontrada: {cadena}");
                    if (cadena.Contains($"src=\"http://www.comunicarseweb.com.ar/") || cadena.Contains($"src=\"https://www.comunicarseweb.com.ar/"))
                    {
                        int startIndex = cadena.IndexOf("/sites",StringComparison.Ordinal) + 6; // 5 es la longitud de "src=\""
                        int endIndex = cadena.IndexOf('"',startIndex);
                        //cambio el src por la url de wordpress
                        string nuevaCadena = cadena.Substring(0, startIndex) +
                             "wp-content/uploads/" + image.Filename +
                            cadena.Substring(endIndex + 1);
                        currentContent = currentContent!.Replace(cadena, nuevaCadena);
                        //chequeo si ya esta mapeada
                        if (!_mappingService.MediaMapping.TryGetValue(image.Fid, out int existingWpId))
                        {
                            //la subo si no esta
                            existingWpId = await MigrateFileToWordPressAsync(image);
                            //la agrego al mapeo
                            _mappingService.MediaMapping[image.Fid] = existingWpId;
                        }
                        //cambio el src por la url de wordpress
                        

                    }
                }


                /*
                // paso 1: verificar si esta en la publicacion!
                //me fijo si esta el fid o si esta el nombre del archivo
                var HasFid = CheckFidInContent(currentContent!, image.Fid);
                while (HasFid.Existe)
                {
                    //reemplazo fid por image
                    _logger.LogInfo($"   🖼️ Encontrada imagen en contenido: {image.Filename} (FID: {image.Fid})");
                    
                    //subo si no esta mapeada
                    //reemplazo el fid por la imagen en el contenido

                    //cargo al map
                }
                */
            }

        }
    }

    public List<string> ObtenerCadenas(string NombreArchivo, string Contenido)
    {
        List<string> cadenas = [];
        int posicion = Contenido.IndexOf(NombreArchivo);
        while (posicion > 0)
        {
            int posicioninicial = -1;
            int posicionfinal = -1;
            for (int i = posicion; i >= 0; i--)
            {
                if (Contenido[i] == '<')
                {
                    posicioninicial = i;
                    break;
                }
            }
            if (posicioninicial >= 0)
            {
                for (int i = posicion + NombreArchivo.Length; i < Contenido.Length; i++)
                {
                    if (Contenido[i] == '>')
                    {
                        posicionfinal = i;
                        break;
                    }
                }
            }
            if (posicionfinal >= 0 && posicioninicial >= 0)
            {
                cadenas.Add(Contenido.Substring(posicioninicial, posicionfinal - posicioninicial + 1));

            }
            posicion = Contenido.IndexOf(NombreArchivo, posicion + 1);
        }
        return cadenas;
    }

    private async Task<int> MigrateFileToWordPressAsync(PostFile file)
    {
        try
        {
            var drupalPath = file.Uri.Replace("public://", "");
            var sourcePath = Path.Combine(ConfiguracionGeneral.DrupalFileRoute, drupalPath);

            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning($"Archivo no encontrado: {sourcePath}");
                return 0;
            }

            // Usar la API de WordPress para subir el archivo
            using var fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            var mediaItem = await _wpClient.Media.CreateAsync(fileStream, file.Filename);

            return mediaItem.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error migrando archivo {file.Filename}: {ex.Message}");
            return 0;
        }
    }

}
