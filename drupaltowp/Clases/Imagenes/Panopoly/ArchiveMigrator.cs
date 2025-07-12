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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL;

namespace drupaltowp.Clases.Imagenes.Panopoly;

internal class ArchiveMigrator
{
    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly MySqlConnection wpConnection = new (ConfiguracionGeneral.WPconnectionString);
    private readonly MappingService _mappingService;

    public ArchiveMigrator(LoggerViewModel logger)
    {
        _logger = logger;
        _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
        _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);
        _mappingService = new(_logger);
    }

    public async Task Migrator(CancellationToken cancellationToken = default)
    {
        _logger.LogProcess("INICIANDO MIGRACION INTELIGENTE DE ARCHIVOS PANOPOLY");
        _logger.LogInfo($"Solo post migrados y con fecha {ConfiguracionGeneral.FechaMinimaImagen.Year}");
        try
        {
            // 🚀 CARGAR MAPEOS EXISTENTES EN MEMORIA
            await _mappingService.LoadBasicMappingsAsync(ContentType.Biblioteca);
            // 2. Obtener posts migrados con sus archivos
            var migratedPosts = await GetMigratedPostsWithAllFilesAsync();
            _logger.LogInfo($"Posts migrados encontrados: {migratedPosts.Count:N0}");
            //3. Filtrar solo los que tienen archivos
            var PostsWithFiles = migratedPosts.Where(p => p.Documents.Count > 0).ToList();
            _logger.LogInfo($"Posts con archivos encontrados: {PostsWithFiles.Count:N0}");
            //4. Proceso cada publicacion.
            int EstaEnContenido = 0;
            int SinContenido = 0;
            int count = 0;
            int total = PostsWithFiles.Count;
            foreach(var post in PostsWithFiles)
            {
                //obtengo el contenido
                var currentContent = await wpConnection.QueryFirstOrDefaultAsync<string>(
                    "SELECT post_content FROM wp_posts WHERE ID = @postId", new { postId = post.WpPostId });
                //por cada archivo, chequeo si esta en el contenido o no
                string newContent = currentContent!;
                List<string> ArchivosIncrustados = [];
                foreach (var archivos in post.Documents)
                {
                    ArchivosIncrustados = [];
                    if (newContent.Contains(archivos.Filename))
                    {
                        newContent = newContent.Replace("sites/default/files", "wp-content/uploads");
                        //Subo el archivo
                        await SubirArchivo(archivos);
                        EstaEnContenido++;
                    }
                    else
                    {
                        //subo el archivo
                        //Agrego una para bajar los archivos
                        await SubirArchivo(archivos);
                        ArchivosIncrustados.Add(archivos.Filename);
                        SinContenido++;
                    }
                }
                if (ArchivosIncrustados.Count > 0)
                {
                    //agrego el contenido al final
                    newContent += PrepareContent(ArchivosIncrustados);
                }
                if (newContent != currentContent)
                {
                    await wpConnection.ExecuteAsync(
                    "UPDATE wp_posts SET post_content = @content WHERE ID = @postId",
                    new { content = currentContent, postId = post.WpPostId });

                    _logger.LogSuccess($"✅ Contenido actualizado archivos: {post.PostTitle}");
                }
                count++;
                if (count % 50== 0)
                {
                    var percentage = (count * 100.0) / total;
                    _logger.LogInfo($"Procesando {count} de {total} ({percentage:F1}%)");
                }
            }
            _logger.LogInfo($"Archivos en contenido {EstaEnContenido}");
            _logger.LogInfo($"Archivos externos {SinContenido}");
        }
        catch (OperationCanceledException)
        {
            _logger.LogMessage($"\n❌ Proceso CANCELADO");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error en migracion inteligente: {ex.Message}");
            throw;
        }
    }

    private string PrepareContent(List<string> archivos)
    {
        var html = new StringBuilder();

        html.AppendLine("\n<hr style=\"margin: 2em 0;\">");
        html.AppendLine("<div class=\"download-section\" style=\"background: #f9f9f9; padding: 1.5em; border-radius: 8px; margin: 2em 0;\">");
        html.AppendLine("<h4 style=\"margin-top: 0; color: #333;\">📎 Archivos para descargar</h4>");
        html.AppendLine("<ul style=\"list-style: none; padding: 0; margin: 0;\">");

        foreach (var doc in archivos)
        {

            html.AppendLine($"<li style=\"margin: 0.5em 0; padding: 0.5em; background: white; border-radius: 4px; border-left: 4px solid #0073aa;\">");
            html.AppendLine($"  <a href=\"https://comunicarseweb.com/wp-content/uploads/{doc}\" target=\"_blank\" rel=\"noopener\" style=\"text-decoration: none; font-weight: 500; color: #0073aa;\">");
            html.AppendLine($"    {doc}");
            html.AppendLine($"  </a>");
            html.AppendLine($"</li>");
        }

        html.AppendLine("</ul>");
        html.AppendLine("<p style=\"margin-bottom: 0; font-size: 0.9em; color: #666; font-style: italic;\">");
        html.AppendLine("Haz clic en cualquier archivo para descargarlo.");
        html.AppendLine("</p>");
        html.AppendLine("</div>");


        return html.ToString();
    }

    private async Task<int> SubirArchivo(PostFile archivo)
    {
        if (!_mappingService.MediaMapping.TryGetValue(archivo.Fid, out int existingWpId))
        {
            //la subo si no esta
            existingWpId = await MigrateFileToWordPressAsync(archivo);
            //la agrego al mapeo
            _mappingService.MediaMapping[archivo.Fid] = existingWpId;
            await SaveFileMapping(archivo.Fid, existingWpId, archivo.Filename);
        }
        return existingWpId;
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

    private string BuscarCadenaAModificar(string contenido, string nombreArchivo)
    {
        int posnombre = contenido.IndexOf(nombreArchivo);
        int posinicial = -1;
        int posfinal = -1;
        for (int i = posnombre;i>0;i--)
        {
            if (contenido[i] == '<')
            {
                posinicial = i;
                break;
            }
        }
        if (posinicial == -1) return "";
        for (int i = posnombre;i<contenido.Length;i++)
        {
            if (contenido[i] == '>')
            {
                posfinal = i;
                break;
            }
        }
        if (posfinal == -1) return "";
        return contenido.Substring(posinicial, posfinal - posinicial + 1);
    }
    private async Task<List<MigratedPostWithImage>> GetMigratedPostsWithAllFilesAsync()
    {
        using var wpConnection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await wpConnection.OpenAsync();
        // PASO 1: OBTENER POSTS MIGRADOS DESDE WORDPRESS
        var simplifiedQuery = @"
               SELECT 
            pm.drupal_post_id as DrupalPostId,    
            pm.wp_post_id as WpPostId,                
            wp.post_date as PostDate,
            wp.post_title as PostTitle
        FROM post_mapping_panopoly  pm                     
        JOIN wp_posts wp ON pm.wp_post_id = wp.ID 
        ORDER BY wp.post_date DESC";

        var posts = await wpConnection.QueryAsync<MigratedPostWithImage>(simplifiedQuery);
        var postList = posts.ToList();

        // 2. Para cada post, obtener TODOS sus archivos usando file_usage
        using var drupalConnection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
        await drupalConnection.OpenAsync();
        int cant = 0;
        int total = postList.Count;
        foreach (var post in postList)
        {
            // Obtener todos los archivos del post
            var files = await drupalConnection.QueryAsync<PostFile>(@"
            SELECT 
                fu.fid,
                f.filename,
                f.uri,
                f.filemime,
                f.filesize,
                fu.module,
                CASE 
                    WHEN f.filemime LIKE 'image/%' THEN 'image'
                    WHEN f.filemime LIKE 'application/pdf' THEN 'pdf'
                    WHEN f.filemime LIKE 'application/%' THEN 'document'
                    ELSE 'other'
                END as FileType,
                -- Verificar si es imagen destacada
                CASE WHEN EXISTS(
                    SELECT 1 FROM field_data_field_featured_image img 
                    WHERE img.entity_id = fu.id AND img.field_featured_image_fid = fu.fid
                ) THEN 1 ELSE 0 END as IsFeaturedImage
            FROM file_usage fu
            JOIN file_managed f ON fu.fid = f.fid
            WHERE fu.type = 'node' 
            AND fu.id = @drupalPostId
            AND f.status = 1
            ORDER BY IsFeaturedImage DESC, fu.fid",
                new { drupalPostId = post.DrupalPostId });

            post.Files = files.ToList();

            // Separar por tipos para fácil acceso
            post.Images = post.Files.Where(f => f.FileType == "image").ToList();
            post.FeaturedImage = post.Images.FirstOrDefault(f => f.IsFeaturedImage);
            post.ContentImages = post.Images.Where(f => !f.IsFeaturedImage).ToList();
            post.Documents = post.Files.Where(f => f.FileType == "pdf" || f.FileType == "document").ToList();
            cant++;
            if (cant % 100 == 0)
            {
                var percentage = (cant * 100.0) / total;
                _logger.LogInfo($"traidos {cant} de {postList.Count} ({percentage:F1}%)");
            }
        }
        return postList;
    }

}
