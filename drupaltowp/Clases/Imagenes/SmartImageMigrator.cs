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
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Models;

namespace drupaltowp.Clases.Imagenes
{
    internal class SmartImageMigrator
    {
        private readonly LoggerViewModel _logger;
        private int? _genericImageId;
        private readonly WordPressClient _wpClient;

        public SmartImageMigrator(LoggerViewModel logger)
        {
            _logger = logger;
            _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
            _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);
        }

        public async Task<ImageMigrationSummary> SmartMigrateImagesAsync()
        {
            var summary = new ImageMigrationSummary { StartTime = DateTime.Now};
            _logger.LogProcess("INICIANDO MIGRACION INTELIGENTE DE IMAGENES");
            _logger.LogInfo($"Solo post migrados y con fecha {ConfiguracionGeneral.FechaMinimaImagen.Year}");

            try
            {
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
                if (postsNeedingGeneric.Count > 0)
                {
                    await AssignGenericImageToPostsAsync(postsNeedingGeneric);
                }

                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en migracion inteligente: {ex.Message}");
                summary.EndTime = DateTime.Now;
                throw;
            }
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
            wp.post_date as PostDate              
        FROM post_mapping_biblioteca  pm                     
        JOIN wp_posts wp ON pm.wp_post_id = wp.ID 
        ORDER BY wp.post_date DESC";

            var posts = await wpConnection.QueryAsync<MigratedPostWithImage>(simplifiedQuery);
            var postList = posts.ToList();

            // 2. Para cada post, obtener TODOS sus archivos usando file_usage
            using var drupalConnection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
            await drupalConnection.OpenAsync();
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
            }
            return postList;
        }
        #region imagen generica
        private async Task EnsureGenericImageExistsAsync()
        {
            _logger.LogProcess("Verificando imagen generica...");

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
}
