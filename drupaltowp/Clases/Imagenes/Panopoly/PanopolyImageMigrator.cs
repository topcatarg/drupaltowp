using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Models;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Client;
using WordPressPCL.Models;
using WordPressPCL.Utility;
using ZstdSharp.Unsafe;

namespace drupaltowp.Clases.Imagenes.Panopoly;

internal class PanopolyImageMigrator
{

    private readonly LoggerViewModel _logger;
    private int? _genericImageId;
    private readonly WordPressClient _wpClient;
    private Dictionary<int, int> _mediaMapping = [];
    private bool MigrarGenerico = false;
    private bool MigrarFeaturedImage = false;
    private bool MigrarOtrasImagenes = false;
    private bool MigrarFidImages = true;
    private MappingService _mappingService;
    public PanopolyImageMigrator(LoggerViewModel logger)
    {
        _logger = logger;
        _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
        _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);
        _mappingService = new(_logger);
    }

    public async Task<ImageMigrationSummary> SmartMigrateImagesAsync(CancellationToken cancellationToken = default)
    {
        var summary = new ImageMigrationSummary { StartTime = DateTime.Now };
        _logger.LogProcess("INICIANDO MIGRACION INTELIGENTE DE IMAGENES PANOPOLY");
        _logger.LogInfo($"Solo post migrados y con fecha {ConfiguracionGeneral.FechaMinimaImagen.Year}");
        try
        {
            // 🚀 CARGAR MAPEOS EXISTENTES EN MEMORIA
            await _mappingService.LoadBasicMappingsAsync(ContentType.PanopolyPage);
            //Verifico la imagen generica
            await EnsureGenericImageExistsAsync();

            // 2. Obtener posts migrados con sus imágenes
            var migratedPosts = await GetMigratedPostsWithAllFilesAsync();
            summary.TotalPostsProcessed = migratedPosts.Count;
            _logger.LogInfo($"Posts migrados encontrados: {migratedPosts.Count:N0}");

            var postsNeedingGeneric = migratedPosts.Where(p => p.NeedsGenericImage).ToList();
            var postsWithFiles = migratedPosts.Where(p => p.Files?.Count > 0).ToList();

            summary.PostsWithGenericImage = postsNeedingGeneric.Count;
            summary.PostsWithOriginalImage = postsWithFiles.Count;

            _logger.LogInfo($"Posts < 2022 (necesitan imagen genérica): {postsNeedingGeneric.Count:N0}");
            _logger.LogInfo($"Posts con archivos a migrar: {postsWithFiles.Count:N0}");

            // Estadísticas detalladas de archivos
            var totalFiles = postsWithFiles.Sum(p => p.Files?.Count ?? 0);
            var totalImages = postsWithFiles.Sum(p => p.Images?.Count ?? 0);
            var totalDocuments = postsWithFiles.Sum(p => p.Documents?.Count ?? 0);
            var postsWithFeaturedImage = postsWithFiles.Count(p => p.FeaturedImage != null);

            _logger.LogInfo($"Total archivos a migrar: {totalFiles:N0}");
            _logger.LogInfo($"Imágenes: {totalImages:N0}");
            _logger.LogInfo($"Documentos: {totalDocuments:N0}");
            _logger.LogInfo($"Posts con imagen destacada: {postsWithFeaturedImage:N0}");
            
            // 4. Asignar imagen genérica a posts antiguos (independiente de si tienen archivos)
            if (MigrarGenerico)
            {
                if (postsNeedingGeneric.Count > 0)
                {
                    await AssignGenericImageToPostsAsync(postsNeedingGeneric);
                }
            }
            // Migrar imagenes destacada
            if (MigrarFeaturedImage)
            {
                var FeaturedImagePostsList = postsWithFiles.Where(p => p.FeaturedImage != null && !p.NeedsGenericImage).ToList();
                FeaturedImageMigrator featuredImageMigrator = new(_logger, FeaturedImagePostsList, _mappingService, _wpClient, cancellationToken);
                await featuredImageMigrator.FeaturedImageProcessor();
            }
            if (MigrarOtrasImagenes)
            {
                OtherImagesMigrator CI = new(_logger, postsWithFiles, _mappingService, _wpClient, cancellationToken);
                await CI.ImageProcessorAsync();
            }
            if (MigrarFidImages)
            {
                FidImageMigrator fidImageMigrator = new(_logger, postsWithFiles, _mappingService, _wpClient, cancellationToken);
                await fidImageMigrator.ImageProcessorAsync();
            }
            /*
            // 5. Migrar archivos de TODOS los posts que tengan archivos (antiguos y nuevos)
            if (postsWithFiles.Count > 0)
            {
                var migrationResult = await ProcessOriginalFilesAsync(postsWithFiles, cancellationToken);
                summary.FilesProcessed = migrationResult.FilesProcessed;
                summary.FilesCopied = migrationResult.FilesCopied;
                summary.FilesSkipped = migrationResult.FilesSkipped;
            }*/


            return summary;
        }
        catch (OperationCanceledException)
        {
            _logger.LogMessage($"\n❌ Proceso CANCELADO");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error en migracion inteligente: {ex.Message}");
            summary.EndTime = DateTime.Now;
            throw;
        }
    }

    private async Task<(int FilesProcessed, int FilesCopied, int FilesSkipped)> ProcessOriginalFilesAsync(
    List<MigratedPostWithImage> posts, CancellationToken cancellationToken)
    {
        _logger.LogProcess($"Procesando archivos de {posts.Count:N0} posts...");

        int filesProcessed = 0, filesCopied = 0, filesSkipped = 0;

        using var wpConnection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await wpConnection.OpenAsync();

        //Paso 1 Proceso las imagenes que son feature
        var FeaturedImageList = posts.Where(x => !x.NeedsGenericImage && x.FeaturedImage != null).ToList();
        if (FeaturedImageList.Count > 0)
        {
            FeaturedImageMigrator FI = new(_logger, FeaturedImageList, _mappingService, _wpClient, cancellationToken);
            await FI.FeaturedImageProcessor();
        }
        //Paso 2, Proceso las imagenes restantes
        var ImageList = posts.Where(x => x.ContainsImages).ToList();
        if (ImageList.Count > 0)
        {
            OtherImagesMigrator CI = new(_logger, ImageList, _mappingService, _wpClient, cancellationToken);
            await CI.ImageProcessorAsync();
        }
        /*
        foreach (var post in posts)
        {
            try
            {
                // PASO 1: Procesar imagen destacada si existe y corresponde
                if (!post.NeedsGenericImage && post.FeaturedImage != null)
                {
                    filesProcessed++;

                    var result = await ProcessFeaturedImageAsync(wpConnection, post, post.FeaturedImage);

                    if (result.WasCopied)
                        filesCopied++;
                    else if (result.WasSkipped)
                        filesSkipped++;
                }

                // 🚀 PASO 2: Procesar imágenes del contenido
                var contentUpdateResult = await ProcessContentImagesAsync(wpConnection, post);
                filesProcessed += contentUpdateResult.ImagesProcessed;
                filesCopied += contentUpdateResult.ImagesCopied;
                filesSkipped += contentUpdateResult.ImagesSkipped;

                // Log progreso cada 25 posts
                if (filesProcessed % 25 == 0)
                {
                    _logger.LogProgress("Procesando archivos", filesProcessed,
                        posts.Sum(p => p.Files?.Count ?? 0), 25);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error procesando post {post.PostTitle}: {ex.Message}");
            }
        }
        // 🚀 PASO 3: Procesar documentos/archivos adjuntos (delegado a DocumentMigrator)
        var _documentMigrator = new DocumentMigrator(_logger, _wpClient, _mediaMapping);

        var documentsResult = await _documentMigrator.ProcessDocumentsAsync(posts);
        filesProcessed += documentsResult.DocumentsProcessed;
        filesCopied += documentsResult.DocumentsCopied;
        filesSkipped += documentsResult.DocumentsSkipped;

        _logger.LogSuccess($"Archivos: {filesCopied:N0} migrados, {filesSkipped:N0} ya existían");
        */
        return (filesProcessed, filesCopied, filesSkipped);
    }

    private async Task AssignGenericImageToPostsAsync(List<MigratedPostWithImage> posts)
    {
        if (!_genericImageId.HasValue)
        {
            _logger.LogWarning("No hay imagen genérica disponible para asignar");
            return;
        }

        _logger.LogProcess($"Asignando imagen genérica a {posts.Count:N0} posts antiguos...");

        using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await connection.OpenAsync();

        const int batchSize = 500; // Lotes más grandes para mejor rendimiento
        int processed = 0;

        for (int i = 0; i < posts.Count; i += batchSize)
        {
            var batch = posts.Skip(i).Take(batchSize);

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                foreach (var post in batch)
                {
                    // Solo asignar imagen genérica si NO tiene imagen destacada propia

                    await connection.ExecuteAsync(@"
                                INSERT INTO wp_postmeta (post_id, meta_key, meta_value) 
                                VALUES (@postId, '_thumbnail_id', @imageId)
                                ON DUPLICATE KEY UPDATE meta_value = @imageId",
                        new { postId = post.WpPostId, imageId = _genericImageId }, transaction);


                    processed++;
                }

                await transaction.CommitAsync();
                _logger.LogProgress("Asignando imagen genérica", processed, posts.Count, 100);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        _logger.LogSuccess($"Imagen genérica asignada a posts antiguos sin imagen propia");
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
            if (cant % 100 == 0 )
            {
                var percentage = (cant * 100.0) / total;
                _logger.LogInfo($"traidos {cant} de {postList.Count} ({percentage:F1}%)");
            }
        }
        return postList;
    }

    #region Check Content Images

    public async Task CheckImageOnContent ()
    {

    }
    #endregion
    #region Generic Image
    private async Task EnsureGenericImageExistsAsync()
    {
        _logger.LogProcess("Verificando imagen generica...");
        if (ConfiguracionGeneral.IdImagenGenerica > -1)
        {
            _genericImageId = ConfiguracionGeneral.IdImagenGenerica;
            _logger.LogSuccess($"Imagen genérica predefinida: ID {_genericImageId}");
            return;
        }
        try
        {
            // Buscar si ya existe la imagen genérica por nombre
            var existingMedia = await _wpClient.Media.GetAllAsync();
            var genericImage = existingMedia.FirstOrDefault(m =>
                m.Title?.Rendered?.Contains("generic-post-image") == true ||
                m.Slug?.Contains("generic-post-image") == true);
            if (genericImage != null)
            {
                _genericImageId = genericImage.Id;
                _logger.LogSuccess($"Imagen genérica encontrada: ID {_genericImageId}");
                return;
            }
            _genericImageId = await CreateGenericImageUsingApiAsync();
            if (_genericImageId.HasValue)
            {
                _logger.LogSuccess($"Imagen genérica creada: ID {_genericImageId}");
            }
            else
            {
                _logger.LogWarning("No se pudo crear imagen genérica, continuando sin ella");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error con imagen genérica: {ex.Message}");
        }


    }

    private async Task<int?> CreateGenericImageUsingApiAsync()
    {
        try
        {
            //Ya se la ruta de origen
            var genericImagePath = Path.Combine(ConfiguracionGeneral.DrupalFileRoute, ConfiguracionGeneral.DrupalGenericImageFileName);

            // Subir usando la API de WordPress
            using var fileStream = new FileStream(genericImagePath, FileMode.Open, FileAccess.Read);
            var mediaItem = await _wpClient.Media.CreateAsync(fileStream, "generic-post-image.jpg");

            // Actualizar metadatos para identificarla fácilmente
            mediaItem.Title = new Title("Imagen Genérica para Posts Antiguos");
            mediaItem.AltText = "Imagen genérica para posts anteriores a 2022";
            mediaItem.Description = new Description("Imagen por defecto asignada automáticamente a posts antiguos durante la migración");

            await _wpClient.Media.UpdateAsync(mediaItem);

            return mediaItem.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creando imagen genérica: {ex.Message}");
            return null;
        }
    }

    #endregion
}
