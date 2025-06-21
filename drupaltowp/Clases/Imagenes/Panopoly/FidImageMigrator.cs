using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Models;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Models;

namespace drupaltowp.Clases.Imagenes.Panopoly;

internal class FidImageMigrator(LoggerViewModel _logger, List<MigratedPostWithImage> PostList, MappingService _mappingService, WordPressClient _wpClient, CancellationToken cancellationToken)
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
            string originalContent = currentContent; // Guardar el contenido original para comparar después
            foreach (var image in post.ContentImages)
            {
                var HasFid = CheckFidInContent(currentContent!, image.Fid);
                if (HasFid.Existe)
                {
                    //reemplazo fid por image
                    _logger.LogInfo($"   🖼️ Encontrada imagen en contenido: {image.Filename} (FID: {image.Fid})");
                    //subo si no esta mapeada
                    //reemplazo el fid por la imagen en el contenido
                    //cargo al map
                    if (!_mappingService.MediaMapping.TryGetValue(image.Fid, out int existingWpId))
                    {
                        _logger.LogInfo($"   🖼️ Imagen no mapeada, subiendo: {image.Filename} (FID: {image.Fid})");
                        //la subo si no esta
                        existingWpId = await MigrateFileToWordPressAsync(image);
                        //la agrego al mapeo
                        await SaveFileMapping(image.Fid, existingWpId, image.Filename);
                    }
                    if (existingWpId == 0)
                    {
                        _logger.LogError($"   ❌ Error al subir imagen: {image.Filename} (FID: {image.Fid})");
                        continue; // Si falla la subida, saltar a la siguiente imagen
                    }

                    string htmlImage = GenerateWordPressImageHtml(await _wpClient.Media.GetByIDAsync(existingWpId), image);
                    //cambio el src por la url de wordpress
                    currentContent = currentContent!.Replace(HasFid.Content, htmlImage);

                }
            }
            //subir el nuevo contenido con la imagen reemplazada
            if (originalContent != currentContent)
            {
                await wpConnection.ExecuteAsync(
                    "UPDATE wp_posts SET post_content = @content WHERE ID = @postId",
                    new { content = currentContent, postId = post.WpPostId });

                _logger.LogSuccess($"✅ Contenido actualizado imágenes: {post.PostTitle}");
            }

        }
    }
    public (bool Existe, string Content) CheckFidInContent(string content, int fid)
    {
        bool existe = content.Contains($"\"fid\":\"{fid}\"");
        string html = FindJsonBlockContainingFid(content, fid);
        // Verifica si el contenido contiene el fid
        return (existe, html);
    }

    private string FindJsonBlockContainingFid(string content, int fid)
    {
        try
        {
            int fidPosition = content.IndexOf($"\"fid\":\"{fid}\"", StringComparison.OrdinalIgnoreCase);
            // 🎯 ESTRATEGIA: Buscar [[ hacia atrás y ]] hacia adelante desde la posición del FID

            // PASO 1: Buscar hacia atrás para encontrar el inicio del bloque [[
            int startIndex = -1;
            for (int i = fidPosition; i >= 1; i--)
            {
                if (content[i] == '[' && content[i - 1] == '[')
                {
                    startIndex = i - 1; // Incluir ambos corchetes
                    break;
                }
            }

            if (startIndex == -1)
            {
                _logger.LogWarning($"   ⚠️ No se encontró [[ antes del FID {fid}");
                return null;
            }

            // PASO 2: Buscar hacia adelante para encontrar el final del bloque ]]
            int endIndex = -1;
            for (int i = fidPosition; i < content.Length - 1; i++)
            {
                if (content[i] == ']' && content[i + 1] == ']')
                {
                    endIndex = i + 1; // Incluir ambos corchetes
                    break;
                }
            }

            if (endIndex == -1)
            {
                _logger.LogWarning($"   ⚠️ No se encontró ]] después del FID {fid}");
                return null;
            }

            // PASO 3: Extraer el bloque completo
            var jsonBlock = content.Substring(startIndex, endIndex - startIndex + 1);

            _logger.LogInfo($"   ✅ Bloque extraído para FID {fid}: inicio={startIndex}, fin={endIndex}");

            return jsonBlock;
        }
        catch (Exception ex)
        {
            _logger.LogError($"   ❌ Error extrayendo bloque JSON para FID {fid}: {ex.Message}");
            return null;
        }
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

    private string GenerateWordPressImageHtml(MediaItem wpMedia, PostFile originalFile)
    {
        // Obtener dimensiones si están disponibles
        var width = wpMedia.MediaDetails?.Width.ToString() ?? "";
        var height = wpMedia.MediaDetails?.Height.ToString() ?? "";

        // Generar alt text inteligente
        var altText = !string.IsNullOrEmpty(wpMedia.AltText)
            ? wpMedia.AltText
            : !string.IsNullOrEmpty(wpMedia.Title?.Rendered)
                ? wpMedia.Title.Rendered
                : Path.GetFileNameWithoutExtension(originalFile.Filename);

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

    private async Task SaveFileMapping(int drupalFid, int wpMediaId, string filename)
    {
        try
        {
            // 1️⃣ GUARDAR EN BASE DE DATOS
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(@"
            INSERT INTO media_mapping (drupal_file_id, wp_media_id, drupal_filename, migrated_at) 
            VALUES (@drupalFid, @wpMediaId, @filename, NOW())
            ON DUPLICATE KEY UPDATE 
                wp_media_id = @wpMediaId, 
                migrated_at = NOW()",
                new { drupalFid, wpMediaId, filename });

            // 2️⃣ ACTUALIZAR CACHE LOCAL (mantener sincronizado)
            _mappingService.MediaMapping[drupalFid] = wpMediaId;

            _logger.LogInfo($"💾 Mapeo guardado: Drupal FID {drupalFid} → WP ID {wpMediaId} ({filename})");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error guardando mapeo para {filename}: {ex.Message}");
            throw; // Re-lanzar para que el llamador sepa que falló
        }
    }

}
