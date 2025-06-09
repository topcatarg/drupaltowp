using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Models;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WordPressPCL;

namespace drupaltowp.Clases.Imagenes;

internal class DocumentMigrator
{
    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly Dictionary<int, int> _mediaMapping;

    public DocumentMigrator(LoggerViewModel logger, WordPressClient wpClient, Dictionary<int, int> mediaMapping)
    {
        _logger = logger;
        _wpClient = wpClient;
        _mediaMapping = mediaMapping;
    }

    /// <summary>
    /// Procesar documentos/archivos adjuntos de múltiples posts
    /// </summary>
    public async Task<(int DocumentsProcessed, int DocumentsCopied, int DocumentsSkipped)> ProcessDocumentsAsync(
        List<MigratedPostWithImage> posts)
    {
        _logger.LogProcess($"🗂️ Procesando documentos/archivos adjuntos de {posts.Count:N0} posts...");

        int documentsProcessed = 0, documentsCopied = 0, documentsSkipped = 0;

        using var wpConnection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await wpConnection.OpenAsync();

        foreach (var post in posts)
        {
            try
            {
                if (post.Documents?.Count > 0)
                {
                    var result = await ProcessPostDocumentsAsync(wpConnection, post);
                    documentsProcessed += result.DocumentsProcessed;
                    documentsCopied += result.DocumentsCopied;
                    documentsSkipped += result.DocumentsSkipped;
                }

                // Log progreso cada 25 posts
                if (documentsProcessed > 0 && documentsProcessed % 25 == 0)
                {
                    _logger.LogProgress("Procesando documentos", documentsProcessed,
                        posts.Sum(p => p.Documents?.Count ?? 0), 25);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error procesando documentos del post {post.PostTitle}: {ex.Message}");
            }
        }

        _logger.LogSuccess($"Documentos: {documentsCopied:N0} migrados, {documentsSkipped:N0} ya existían");
        return (documentsProcessed, documentsCopied, documentsSkipped);
    }

    /// <summary>
    /// Procesar todos los documentos de un post específico
    /// </summary>
    private async Task<(int DocumentsProcessed, int DocumentsCopied, int DocumentsSkipped)> ProcessPostDocumentsAsync(
        MySqlConnection wpConnection, MigratedPostWithImage post)
    {
        int documentsProcessed = 0, documentsCopied = 0, documentsSkipped = 0;

        if (post.Documents?.Count == 0)
            return (0, 0, 0);

        _logger.LogInfo($"📎 Procesando {post.Documents.Count} documentos para: {post.PostTitle}");

        var copiedDocuments = new List<CopiedDocumentInfo>();

        foreach (var document in post.Documents)
        {
            documentsProcessed++;

            try
            {
                // Verificar si ya está en cache local
                if (_mediaMapping.TryGetValue(document.Fid, out int existingWpId))
                {
                    var existingMedia = await _wpClient.Media.GetByIDAsync(existingWpId);
                    copiedDocuments.Add(new CopiedDocumentInfo
                    {
                        OriginalFile = document,
                        WpMediaId = existingWpId,
                        WpUrl = existingMedia.SourceUrl,
                        WasCopied = false
                    });
                    documentsSkipped++;
                    _logger.LogInfo($"   ♻️ Documento ya existe: {document.Filename}");
                    continue;
                }

                // Copiar documento al sitio WordPress
                var result = await CopyDocumentToWordPressAsync(document);

                if (result != null)
                {
                    copiedDocuments.Add(result);

                    if (result.WasCopied)
                        documentsCopied++;
                    else
                        documentsSkipped++;

                    // Guardar mapeo
                    await SaveFileMapping(document.Fid, result.WpMediaId, document.Filename);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"   ❌ Error procesando documento {document.Filename}: {ex.Message}");
            }
        }

        // Agregar sección de descargas al post si hay documentos procesados
        if (copiedDocuments.Count > 0)
        {
            await AddDownloadSectionToPostAsync(wpConnection, post.WpPostId, copiedDocuments);
            _logger.LogSuccess($"   📎 Sección de descargas agregada con {copiedDocuments.Count} archivos");
        }

        return (documentsProcessed, documentsCopied, documentsSkipped);
    }

    /// <summary>
    /// Agregar sección de descargas al final del contenido del post
    /// </summary>
    private async Task AddDownloadSectionToPostAsync(MySqlConnection wpConnection, int postId, List<CopiedDocumentInfo> documents)
    {
        try
        {
            // Obtener contenido actual del post
            var currentContent = await wpConnection.QueryFirstOrDefaultAsync<string>(
                "SELECT post_content FROM wp_posts WHERE ID = @postId",
                new { postId });

            if (string.IsNullOrEmpty(currentContent))
                return;

            // Verificar si ya tiene sección de descargas
            if (currentContent.Contains("class=\"download-section\""))
            {
                _logger.LogInfo($"   ℹ️ Post ya tiene sección de descargas, omitiendo");
                return;
            }

            // Crear HTML de la sección de descargas
            var downloadSection = BuildDownloadSectionHtml(documents);

            // Agregar al final del contenido
            var updatedContent = currentContent + downloadSection;

            // Actualizar post en base de datos
            await wpConnection.ExecuteAsync(
                "UPDATE wp_posts SET post_content = @content WHERE ID = @postId",
                new { content = updatedContent, postId });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error agregando sección de descargas al post {postId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Construir HTML para la sección de descargas
    /// </summary>
    private string BuildDownloadSectionHtml(List<CopiedDocumentInfo> documents)
    {
        var html = new StringBuilder();

        html.AppendLine("\n<hr style=\"margin: 2em 0;\">");
        html.AppendLine("<div class=\"download-section\" style=\"background: #f9f9f9; padding: 1.5em; border-radius: 8px; margin: 2em 0;\">");
        html.AppendLine("<h4 style=\"margin-top: 0; color: #333;\">📎 Archivos para descargar</h4>");
        html.AppendLine("<ul style=\"list-style: none; padding: 0; margin: 0;\">");

        foreach (var doc in documents)
        {
            var fileIcon = GetDocumentIcon(doc.OriginalFile.FileType, doc.OriginalFile.Filemime);
            var fileSize = FormatFileSize(doc.OriginalFile.Filesize);
            var filename = doc.OriginalFile.Filename;

            html.AppendLine($"<li style=\"margin: 0.5em 0; padding: 0.5em; background: white; border-radius: 4px; border-left: 4px solid #0073aa;\">");
            html.AppendLine($"  <span style=\"font-size: 1.2em; margin-right: 0.5em;\">{fileIcon}</span>");
            html.AppendLine($"  <a href=\"{doc.WpUrl}\" target=\"_blank\" rel=\"noopener\" style=\"text-decoration: none; font-weight: 500; color: #0073aa;\">");
            html.AppendLine($"    {filename}");
            html.AppendLine($"  </a>");
            html.AppendLine($"  <span style=\"color: #666; font-size: 0.9em; margin-left: 0.5em;\">({fileSize})</span>");
            html.AppendLine($"</li>");
        }

        html.AppendLine("</ul>");
        html.AppendLine("<p style=\"margin-bottom: 0; font-size: 0.9em; color: #666; font-style: italic;\">");
        html.AppendLine("Haz clic en cualquier archivo para descargarlo.");
        html.AppendLine("</p>");
        html.AppendLine("</div>");

        return html.ToString();
    }
    /// <summary>
    /// Guardar mapeo de archivo para futuras referencias
    /// </summary>
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
            _mediaMapping[drupalFid] = wpMediaId;

            _logger.LogInfo($"💾 Mapeo guardado: Drupal FID {drupalFid} → WP ID {wpMediaId} ({filename})");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error guardando mapeo para {filename}: {ex.Message}");
            throw; // Re-lanzar para que el llamador sepa que falló
        }
    }

    /// <summary>
    /// Copiar un documento desde Drupal local a WordPress local
    /// </summary>
    private async Task<CopiedDocumentInfo> CopyDocumentToWordPressAsync(PostFile document)
    {
        try
        {
            // Construir ruta de origen (archivo local de Drupal)
            var drupalPath = document.Uri.Replace("public://", "");
            var sourcePath = Path.Combine(ConfiguracionGeneral.DrupalFileRoute, drupalPath);

            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning($"   ⚠️ Archivo no encontrado: {sourcePath}");
                return null;
            }

            // Obtener nombre único verificando si existe en destino
            var uniqueFilename = GetUniqueFilenameInDestination(document.Filename);

            // Construir ruta de destino
            var destinationPath = Path.Combine(ConfiguracionGeneral.WPFileRoute, uniqueFilename);

            // Copiar archivo físicamente
            File.Copy(sourcePath, destinationPath, overwrite: true);

            // Crear entrada en WordPress media library (sin subdirectorio)
            var mediaId = await CreateWordPressMediaEntryAsync(document, uniqueFilename, uniqueFilename);

            // Construir URL de WordPress (directamente en uploads)
            var wpUrl = $"/wp-content/uploads/{uniqueFilename}";

            var result = new CopiedDocumentInfo
            {
                OriginalFile = document,
                CopiedFilename = uniqueFilename,
                WpMediaId = mediaId,
                WpUrl = wpUrl,
                LocalPath = destinationPath,
                WasCopied = true
            };

            _logger.LogInfo($"   📋 Archivo copiado: {document.Filename} -> {uniqueFilename}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"   ❌ Error copiando archivo {document.Filename}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Obtener nombre único verificando si existe en el directorio de destino
    /// Agrega _*01, _*02, etc. si el archivo ya existe
    /// </summary>
    private string GetUniqueFilenameInDestination(string originalFilename)
    {
        var name = Path.GetFileNameWithoutExtension(originalFilename);
        var extension = Path.GetExtension(originalFilename);

        // Primero probar con el nombre original
        var candidateFilename = originalFilename;
        var candidatePath = Path.Combine(ConfiguracionGeneral.WPFileRoute, candidateFilename);

        if (!File.Exists(candidatePath))
        {
            _logger.LogInfo($"   📝 Usando nombre original: {originalFilename}");
            return candidateFilename;
        }

        // Si existe, buscar nombre disponible con sufijo numérico
        int counter = 1;
        do
        {
            candidateFilename = $"{name}_*{counter:D2}{extension}";
            candidatePath = Path.Combine(ConfiguracionGeneral.WPFileRoute, candidateFilename);
            counter++;

            // Safeguard para evitar bucle infinito
            if (counter > 999)
            {
                // Si llegamos a 999, usar timestamp como último recurso
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                candidateFilename = $"{name}_{timestamp}{extension}";
                _logger.LogWarning($"   ⚠️ Muchos archivos duplicados, usando timestamp: {candidateFilename}");
                break;
            }

        } while (File.Exists(candidatePath));

        _logger.LogInfo($"   📝 Archivo renombrado para evitar conflicto: {originalFilename} -> {candidateFilename}");
        return candidateFilename;
    }

    /// <summary>
    /// Crear entrada en la media library de WordPress para el documento
    /// </summary>
    private async Task<int> CreateWordPressMediaEntryAsync(PostFile file, string filename, string relativePath)
    {
        try
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            var now = DateTime.Now;
            var guid = $"/wp-content/uploads/{relativePath}"; // relativePath ya es solo el filename
            var title = Path.GetFileNameWithoutExtension(filename);
            var slug = GenerateSlug(title);

            // Insertar en wp_posts (media library)
            var mediaId = await connection.QueryFirstAsync<int>(@"
                    INSERT INTO wp_posts (
                        post_author, post_date, post_date_gmt, post_content, post_title, 
                        post_excerpt, post_status, comment_status, ping_status, post_password,
                        post_name, to_ping, pinged, post_modified, post_modified_gmt,
                        post_content_filtered, post_parent, guid, menu_order, post_type,
                        post_mime_type, comment_count
                    ) VALUES (
                        1, @date, @date, '', @title, '', 'inherit', 'open', 'closed', '',
                        @slug, '', '', @date, @date, '', 0, @guid, 0, 'attachment',
                        @mimeType, 0
                    );
                    SELECT LAST_INSERT_ID();",
                new
                {
                    date = now,
                    title = title,
                    slug = slug,
                    guid = guid,
                    mimeType = file.Filemime
                });

            // Insertar metadata del archivo (sin subdirectorio)
            await connection.ExecuteAsync(@"
                    INSERT INTO wp_postmeta (post_id, meta_key, meta_value) VALUES
                    (@mediaId, '_wp_attached_file', @attachedFile),
                    (@mediaId, '_wp_attachment_metadata', @metadata)",
                new
                {
                    mediaId,
                    attachedFile = relativePath, // Solo el filename, sin subdirectorio
                    metadata = CreateFileMetadata(relativePath, file)
                });

            return mediaId;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creando entrada de media para {filename}: {ex.Message}");
            throw;
        }
    }

    #region Métodos auxiliares

    /// <summary>
    /// Generar slug para WordPress
    /// </summary>
    private string GenerateSlug(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "archivo";

        var slug = text.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^\w\-_]", "-");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');

        return string.IsNullOrEmpty(slug) ? "archivo" : slug;
    }

    /// <summary>
    /// Crear metadata de archivo para WordPress
    /// </summary>
    private string CreateFileMetadata(string relativePath, PostFile file)
    {
        // Metadata básico para archivos no-imagen
        // Serialización PHP simple para WordPress
        return $"a:2:{{s:4:\"file\";s:{relativePath.Length}:\"{relativePath}\";s:8:\"filesize\";i:{file.Filesize};}}";
    }

    /// <summary>
    /// Obtener icono apropiado para tipo de documento
    /// </summary>
    private string GetDocumentIcon(string fileType, string mimeType)
    {
        // Primero por tipo específico
        return fileType?.ToLowerInvariant() switch
        {
            "pdf" => "📄",
            "document" => mimeType?.ToLowerInvariant() switch
            {
                var mt when mt.Contains("word") => "📝",
                var mt when mt.Contains("excel") || mt.Contains("spreadsheet") => "📊",
                var mt when mt.Contains("powerpoint") || mt.Contains("presentation") => "📊",
                _ => "📄"
            },
            "other" => mimeType?.ToLowerInvariant() switch
            {
                var mt when mt.Contains("zip") || mt.Contains("rar") || mt.Contains("compressed") => "🗜️",
                var mt when mt.Contains("audio") => "🎵",
                var mt when mt.Contains("video") => "🎬",
                var mt when mt.Contains("text") => "📄",
                _ => "📁"
            },
            _ => "📁"
        };
    }

    /// <summary>
    /// Formatear tamaño de archivo para mostrar
    /// </summary>
    private string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    #endregion

}
