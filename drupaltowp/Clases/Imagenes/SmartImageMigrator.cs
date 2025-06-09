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
using System.Windows.Automation;
using WordPressPCL;
using WordPressPCL.Models;

namespace drupaltowp.Clases.Imagenes
{
    internal class SmartImageMigrator
    {
        private readonly LoggerViewModel _logger;
        private int? _genericImageId;
        private readonly WordPressClient _wpClient;
        private Dictionary<int, int> _mediaMapping = [];
        private bool MigrarGenerico = false;
        public SmartImageMigrator(LoggerViewModel logger)
        {
            _logger = logger;
            _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
            _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);
        }

        public async Task<ImageMigrationSummary> SmartMigrateImagesAsync()
        {
            var summary = new ImageMigrationSummary { StartTime = DateTime.Now };
            _logger.LogProcess("INICIANDO MIGRACION INTELIGENTE DE IMAGENES");
            _logger.LogInfo($"Solo post migrados y con fecha {ConfiguracionGeneral.FechaMinimaImagen.Year}");

            try
            {
                // 🚀 CARGAR MAPEOS EXISTENTES EN MEMORIA
                await LoadExistingMediaMappingsAsync();
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

                // 5. Migrar archivos de TODOS los posts que tengan archivos (antiguos y nuevos)
                if (postsWithFiles.Count > 0)
                {
                    var migrationResult = await ProcessOriginalFilesAsync(postsWithFiles);
                    summary.FilesProcessed = migrationResult.FilesProcessed;
                    summary.FilesCopied = migrationResult.FilesCopied;
                    summary.FilesSkipped = migrationResult.FilesSkipped;
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

        private async Task<(int FilesProcessed, int FilesCopied, int FilesSkipped)> ProcessOriginalFilesAsync(
            List<MigratedPostWithImage> posts)
        {
            _logger.LogProcess($"Procesando archivos de {posts.Count:N0} posts...");

            int filesProcessed = 0, filesCopied = 0, filesSkipped = 0;

            using var wpConnection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await wpConnection.OpenAsync();

            foreach (var post in posts)
            {
                try
                {
                    // PASO 1: Procesar imagen destacada si existe y corresponde
                    if (!post.NeedsGenericImage && post.FeaturedImage != null )
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
            return (filesProcessed, filesCopied, filesSkipped);
        }

        /// <summary>
        /// Procesa todas las imágenes encontradas en el contenido de un post
        /// </summary>
        private async Task<(int ImagesProcessed, int ImagesCopied, int ImagesSkipped)> ProcessContentImagesAsync(
            MySqlConnection wpConnection, MigratedPostWithImage post)
        {
            int imagesProcessed = 0, imagesCopied = 0, imagesSkipped = 0;
            if (post.ContentImages.Count == 0)
            {
                _logger.LogInfo($"Post {post.PostTitle} no tiene imágenes en el contenido, omitiendo");
                return (0, 0, 0); // Sin imágenes que procesar
            }

            var currentContent = await wpConnection.QueryFirstOrDefaultAsync<string>(
            "SELECT post_content FROM wp_posts WHERE ID = @postId",
            new { postId = post.WpPostId });

            
            try
            {
                if (string.IsNullOrEmpty(currentContent))
                {
                    return (0, 0, 0); // Sin contenido que procesar
                }

                // 2️⃣ BUSCAR TODAS LAS IMÁGENES DE DRUPAL EN EL CONTENIDO
                var imageMatches = FindDrupalImagesInContent(currentContent);

                if (!imageMatches.Any())
                {
                    return (0, 0, 0); // Sin imágenes que procesar
                }
                _logger.LogInfo($"🖼️ Encontradas {imageMatches.Count} imágenes en contenido de: {post.PostTitle}");

                // 3️⃣ PROCESAR CADA IMAGEN ENCONTRADA
                var updatedContent = currentContent;

                foreach (var imageMatch in imageMatches)
                {
                    imagesProcessed++;

                    var result = await ProcessSingleContentImageAsync(imageMatch);

                    if (result.WasCopied)
                        imagesCopied++;
                    else if (result.WasSkipped)
                        imagesSkipped++;

                    // 4️⃣ ACTUALIZAR URL EN EL CONTENIDO
                    if (!string.IsNullOrEmpty(result.NewUrl))
                    {
                        updatedContent = updatedContent.Replace(imageMatch.OriginalUrl, result.NewUrl);
                        _logger.LogInfo($"   📝 URL actualizada: {imageMatch.Filename}");
                    }
                }
                // 5️⃣ GUARDAR CONTENIDO ACTUALIZADO SI HUBO CAMBIOS
                if (updatedContent != currentContent)
                {
                    await wpConnection.ExecuteAsync(
                        "UPDATE wp_posts SET post_content = @content WHERE ID = @postId",
                        new { content = updatedContent, postId = post.WpPostId });

                    _logger.LogSuccess($"✅ Contenido actualizado con {imageMatches.Count} imágenes: {post.PostTitle}");
                }

                return (imagesProcessed, imagesCopied, imagesSkipped);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error procesando imágenes del contenido en {post.PostTitle}: {ex.Message}");
                return (imagesProcessed, imagesCopied, imagesSkipped);
            }
        }

        private async Task<(bool WasCopied, bool WasSkipped, string? NewUrl)> ProcessSingleContentImageAsync(
        ContentImageMatch imageMatch)
        {
            
            try
            {

                PostFile drupalFile = await FindDrupalFileByFid(imageMatch.Fid.Value);

                if (drupalFile == null)
                {
                    _logger.LogWarning($"   ⚠️ Archivo con FID {imageMatch.Fid.Value} no encontrado en Drupal");
                    return (false, false, null);
                }
                // 2️⃣ VERIFICAR SI YA ESTÁ EN CACHE LOCAL
                if (_mediaMapping.TryGetValue(drupalFile.Fid, out int existingWpId))
                {
                    var existingMedia = await _wpClient.Media.GetByIDAsync(existingWpId);
                    var newHtml = GenerateWordPressImageHtml(existingMedia, drupalFile);

                    _logger.LogInfo($"   ♻️ Imagen del contenido ya existe: {drupalFile.Filename}");
                    return (false, true, newHtml); // Skipped pero devuelve nuevo HTML
                }
                // 3️⃣ MIGRAR IMAGEN NUEVA
                var wpMediaId = await MigrateFileToWordPressAsync(drupalFile);

                if (!wpMediaId.HasValue)
                {
                    return (false, false, null);
                }
                // 4️⃣ GUARDAR MAPEO Y GENERAR HTML
                await SaveFileMapping(drupalFile.Fid, wpMediaId.Value, drupalFile.Filename);

                var newMedia = await _wpClient.Media.GetByIDAsync(wpMediaId.Value);
                var wpImageHtml = GenerateWordPressImageHtml(newMedia, drupalFile);

                _logger.LogInfo($"   ✅ Imagen del contenido migrada: {drupalFile.Filename}");
                return (true, false, wpImageHtml); // Copied y devuelve nuevo HTML
            }
            catch (Exception ex)
            {
                _logger.LogError($"   ❌ Error procesando imagen: {ex.Message}");
                return (false, false, null);
            }




        }

        /// <summary>
        /// Genera HTML de imagen de WordPress estándar
        /// </summary>
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

        /// <summary>
        /// Busca un archivo en Drupal por su FID (más directo y confiable)
        /// </summary>
        private async Task<PostFile> FindDrupalFileByFid(int fid)
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
            await connection.OpenAsync();

            var file = await connection.QueryFirstOrDefaultAsync<PostFile>(@"
        SELECT 
            fid as Fid,
            filename as Filename,
            uri as Uri,
            filemime as Filemime,
            filesize as Filesize,
            'image' as FileType
        FROM file_managed 
        WHERE fid = @fid 
        AND status = 1",
                new { fid });

            return file;
        }

        /// <summary>
        /// Encuentra todas las imágenes de Drupal en el contenido HTML
        /// </summary>
        private List<ContentImageMatch> FindDrupalImagesInContent(string content)
        {
            var imageMatches = new List<ContentImageMatch>();

            // 🎯 PATRÓN 1: Formato JSON específico de Drupal Media (PRINCIPAL)
            // [[{"fid":"26986","view_mode":"default",...}]]
            var fidPattern = @"""fid"":""(\d+)""";
            var fidRegex = new Regex(fidPattern, RegexOptions.IgnoreCase);
            var fidMatches = fidRegex.Matches(content);
            _logger.LogInfo($"🔍 Buscando FIDs en contenido... Patrón: {fidPattern}");
            _logger.LogInfo($"📊 Encontrados {fidMatches.Count} FIDs");
            foreach (Match fidMatch in fidMatches)
            {
                try
                {
                    var fidString = fidMatch.Groups[1].Value;
                    var fid = int.Parse(fidString);

                    _logger.LogInfo($"   📸 FID encontrado: {fid}");
                    // Buscar el bloque JSON completo que contiene este FID
                    var jsonBlock = FindJsonBlockContainingFid(content, fid, fidMatch.Index);

                    if (!string.IsNullOrEmpty(jsonBlock))
                    {
                        _logger.LogInfo($"   📄 Bloque JSON encontrado: {jsonBlock.Substring(0, Math.Min(50, jsonBlock.Length))}...");

                        // Evitar duplicados (si el mismo bloque tiene el FID repetido)
                        if (!imageMatches.Any(im => im.FullMatch == jsonBlock))
                        {
                            imageMatches.Add(new ContentImageMatch
                            {
                                OriginalUrl = jsonBlock, // El JSON completo
                                Filename = $"drupal-media-{fid}",
                                FullMatch = jsonBlock, // Para reemplazo completo
                                Fid = fid,
                                IsDrupalMediaJson = true
                            });
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"   ⚠️ No se pudo encontrar bloque JSON completo para FID {fid}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"   ❌ Error procesando FID: {ex.Message}");
                }
            }
            return imageMatches;
        }

        /// <summary>
        /// Encuentra el bloque JSON completo que contiene un FID específico
        /// Busca hacia atrás y adelante desde la posición del FID
        /// </summary>
        private string FindJsonBlockContainingFid(string content, int fid, int fidPosition)
        {
            try
            {
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

        /// <summary>
        /// Versión optimizada que usa cache en memoria - O(1) lookup
        /// </summary>
        private async Task<(bool WasCopied, bool WasSkipped)> ProcessFeaturedImageAsync(
            MySqlConnection wpConnection, MigratedPostWithImage post, PostFile featuredImage)
        {
            // ✅ PASO 1: Verificar si necesita imagen que NO sea la genérica
            if (post.NeedsGenericImage)
            {
                _logger.LogInfo($"Post {post.PostTitle} usa imagen genérica, omitiendo migración de {featuredImage.Filename}");
                return (false, true); // Skipped - post usa imagen genérica
            }

            // ✅ PASO 2: Verificar si ya está en el CACHE LOCAL (BÚSQUEDA EN MEMORIA O(1))
            if (_mediaMapping.TryGetValue(featuredImage.Fid, out int existingWpId))
            {
                // Ya existe - solo asignar al post
                await AssignFeaturedImageToPostAsync(post.WpPostId, existingWpId);
                _logger.LogInfo($"📸 Imagen destacada existente asignada: {featuredImage.Filename} (ID: {existingWpId})");
                return (false, true); // Skipped - ya existía
            }

            // ✅ PASO 3: No existe - subir, asignar y guardar en mapeo
            var wpMediaId = await MigrateFileToWordPressAsync(featuredImage);

            if (!wpMediaId.HasValue)
            {
                _logger.LogError($"Error subiendo imagen destacada: {featuredImage.Filename}");
                return (false, false); // Error
            }

            // Asignar como imagen destacada del post
            await AssignFeaturedImageToPostAsync(post.WpPostId, wpMediaId.Value);

            // Guardar en BD Y actualizar cache local
            await SaveFileMapping(featuredImage.Fid, wpMediaId.Value, featuredImage.Filename);

            _logger.LogInfo($"📸 Imagen destacada migrada y asignada: {featuredImage.Filename} (ID: {wpMediaId.Value})");

            return (true, false); // Copied - nueva imagen migrada
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
                _mediaMapping[drupalFid] = wpMediaId;

                _logger.LogInfo($"💾 Mapeo guardado: Drupal FID {drupalFid} → WP ID {wpMediaId} ({filename})");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error guardando mapeo para {filename}: {ex.Message}");
                throw; // Re-lanzar para que el llamador sepa que falló
            }
        }

        private async Task<int?> MigrateFileToWordPressAsync(PostFile file)
        {
            try
            {
                var drupalPath = file.Uri.Replace("public://", "");
                var sourcePath = Path.Combine(ConfiguracionGeneral.DrupalFileRoute, drupalPath);

                if (!File.Exists(sourcePath))
                {
                    _logger.LogWarning($"Archivo no encontrado: {sourcePath}");
                    return null;
                }

                // Usar la API de WordPress para subir el archivo
                using var fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
                var mediaItem = await _wpClient.Media.CreateAsync(fileStream, file.Filename);

                return mediaItem.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error migrando archivo {file.Filename}: {ex.Message}");
                return null;
            }
        }
        private async Task AssignFeaturedImageToPostAsync(int postId, int mediaId)
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(@"
                INSERT INTO wp_postmeta (post_id, meta_key, meta_value) 
                VALUES (@postId, '_thumbnail_id', @mediaId)
                ON DUPLICATE KEY UPDATE meta_value = @mediaId",
                new { postId, mediaId });
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

        private async Task LoadExistingMediaMappingsAsync()
        {
            _logger.LogProcess("Cargando mapeos de medios existentes...");

            try
            {
                using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
                await connection.OpenAsync();

                var mappings = await connection.QueryAsync<(int DrupalFid, int WpId)>(
                    "SELECT drupal_file_id, wp_media_id FROM media_mapping");

                _mediaMapping = mappings.ToDictionary(m => m.DrupalFid, m => m.WpId);

                _logger.LogSuccess($"✅ Cargados {_mediaMapping.Count:N0} mapeos existentes en memoria");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error cargando mapeos: {ex.Message}");
                _mediaMapping = [];
            }
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
