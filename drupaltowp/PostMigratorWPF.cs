using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using Dapper;
using MySql.Data.MySqlClient;
using WordPressPCL;
using WordPressPCL.Models;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Windows;

namespace drupaltowp
{
    public class PostMigratorWPF
    {
        private readonly string _drupalConnectionString;
        private readonly string _wpConnectionString;
        private readonly WordPressClient _wpClient;
        private readonly TextBlock _statusTextBlock;
        private readonly ScrollViewer _logScrollViewer;

        // Mappings para mantener referencias entre sistemas
        private Dictionary<int, int> _userMapping = new();
        private Dictionary<int, int> _categoryMapping = new();
        private Dictionary<int, int> _tagMapping = new();
        private Dictionary<int, int> _postMapping = new();

        // Cache de imágenes migradas
        private readonly ConcurrentDictionary<string, int> _imageUrlCache = new();
        private readonly ConcurrentDictionary<int, int> _imageFidCache = new();

        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        public PostMigratorWPF(string drupalConnectionString, string wpConnectionString,
                              WordPressClient wpClient, TextBlock statusTextBlock, ScrollViewer logScrollViewer)
        {
            _drupalConnectionString = drupalConnectionString;
            _wpConnectionString = wpConnectionString;
            _wpClient = wpClient;
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer;
        }

        #region Métodos Principales de Migración

        public async Task<Dictionary<int, int>> MigratePostsAsync()
        {
            LogMessage("🚀 Iniciando migración de posts...");

            try
            {
                // 1. Cargar mappings existentes
                await LoadMappingsAsync();

                // 2. Cargar cache de imágenes ya migradas
                await LoadImageCacheAsync();

                // 3. Obtener posts de Drupal (usando field_featured_image)
                var drupalPosts = await GetDrupalPostsAsync();
                LogMessage($"📊 Encontrados {drupalPosts.Count} posts en Drupal");

                if (drupalPosts.Count == 0)
                {
                    LogMessage("⚠️ No se encontraron posts para migrar");
                    return _postMapping;
                }

                // 4. Migrar cada post
                int migratedCount = 0;
                foreach (var post in drupalPosts)
                {
                    try
                    {
                        await MigratePostAsync(post);
                        migratedCount++;
                        LogMessage($"✅ Migrado post: {post.Title} ({migratedCount}/{drupalPosts.Count})");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"❌ Error migrando post {post.Title}: {ex.Message}");
                    }
                }

                LogMessage($"🎉 Migración completada: {migratedCount} posts migrados");
                return _postMapping;
            }
            catch (Exception ex)
            {
                LogMessage($"💥 Error en migración: {ex.Message}");
                throw;
            }
        }

        private async Task<List<DrupalPost>> GetDrupalPostsAsync()
        {
            using var drupalConnection = new MySqlConnection(_drupalConnectionString);
            await drupalConnection.OpenAsync();

            LogMessage("🔍 Ejecutando consulta de posts con field_data_field_featured_image...");

            // QUERY CORREGIDA - usando field_data_field_featured_image
            var query = @"
                SELECT 
                    n.nid,
                    n.title,
                    n.uid,
                    n.created,
                    n.changed,
                    n.status,
                    b.body_value AS content,
                    b.body_summary AS excerpt,
                    bj.field_bajada_value AS bajada,
                    img.field_featured_image_fid AS image_fid,
                    f.filename AS image_filename,
                    f.uri AS image_uri
                FROM node n
                LEFT JOIN field_data_body b ON n.nid = b.entity_id
                LEFT JOIN field_data_field_bajada bj ON n.nid = bj.entity_id
                LEFT JOIN field_data_field_featured_image img ON n.nid = img.entity_id
                LEFT JOIN file_managed f ON img.field_featured_image_fid = f.fid
                WHERE n.type IN ('article', 'blog', 'story')
                ORDER BY n.created";

            LogMessage("📊 Consulta SQL:");
            LogMessage($"   Query: {query.Replace("\n", " ").Replace("                ", " ")}");

            var posts = await drupalConnection.QueryAsync<DrupalPost>(query);
            var postList = posts.ToList();

            LogMessage($"📋 Resultados de la consulta:");
            LogMessage($"   Total posts encontrados: {postList.Count}");

            // Mostrar estadísticas de imágenes
            var postsWithImages = postList.Where(p => p.ImageFid.HasValue && p.ImageFid > 0).ToList();
            LogMessage($"   Posts con imagen destacada: {postsWithImages.Count}");

            if (postsWithImages.Any())
            {
                LogMessage($"📋 Primeros 3 posts con imagen:");
                foreach (var post in postsWithImages.Take(3))
                {
                    LogMessage($"   - [{post.Nid}] {post.Title}");
                    LogMessage($"     Image FID: {post.ImageFid}, Archivo: {post.ImageFilename}");
                    LogMessage($"     URI: {post.ImageUri}");
                }
            }
            else
            {
                LogMessage("   ⚠️ NINGÚN POST TIENE IMAGEN DESTACADA");
                LogMessage("   ℹ️ Verificar que la tabla field_data_field_featured_image tenga datos");
            }

            // Obtener categorías y tags para cada post
            LogMessage("🔍 Obteniendo categorías y tags...");
            foreach (var post in postList)
            {
                post.Categories = await GetPostCategoriesAsync(drupalConnection, post.Nid);
                post.Tags = await GetPostTagsAsync(drupalConnection, post.Nid);
            }

            LogMessage($"✅ Posts procesados completamente: {postList.Count}");
            return postList;
        }

        private async Task MigratePostAsync(DrupalPost drupalPost)
        {
            // Preparar contenido del post
            var content = await ProcessContentAsync(drupalPost.Content);
            var excerpt = !string.IsNullOrEmpty(drupalPost.Bajada) ? drupalPost.Bajada : drupalPost.Excerpt;

            // Obtener autor de WordPress
            var authorId = _userMapping.ContainsKey(drupalPost.Uid) ? _userMapping[drupalPost.Uid] : 1;

            // Crear post en WordPress
            var wpPost = new Post
            {
                Title = new Title(drupalPost.Title),
                Content = new Content(content),
                Excerpt = new Excerpt(excerpt ?? ""),
                Author = authorId,
                Status = drupalPost.Status == 1 ? Status.Publish : Status.Draft,
                Date = DateTimeOffset.FromUnixTimeSeconds(drupalPost.Created).DateTime,
                Modified = DateTimeOffset.FromUnixTimeSeconds(drupalPost.Changed).DateTime
            };

            // Asignar categorías
            if (drupalPost.Categories?.Any() == true)
            {
                var wpCategories = drupalPost.Categories
                    .Where(catId => _categoryMapping.ContainsKey(catId))
                    .Select(catId => _categoryMapping[catId])
                    .ToList();

                if (wpCategories.Any())
                    wpPost.Categories = wpCategories;
            }

            // Asignar tags
            if (drupalPost.Tags?.Any() == true)
            {
                var wpTags = drupalPost.Tags
                    .Where(tagId => _tagMapping.ContainsKey(tagId))
                    .Select(tagId => _tagMapping[tagId])
                    .ToList();

                if (wpTags.Any())
                    wpPost.Tags = wpTags;
            }

            // Crear el post
            var createdPost = await _wpClient.Posts.CreateAsync(wpPost);

            // Procesar imagen destacada si existe
            if (drupalPost.ImageFid.HasValue && drupalPost.ImageFid > 0)
            {
                await SetFeaturedImageAsync(createdPost.Id, drupalPost.ImageFid.Value, drupalPost.ImageUri, drupalPost.ImageFilename);
            }

            // Guardar mapping
            await SavePostMappingAsync(drupalPost.Nid, createdPost.Id);
            _postMapping[drupalPost.Nid] = createdPost.Id;
        }

        #endregion

        #region Métodos de Verificación y Estado

        public async Task<bool> CheckPrerequisitesAsync()
        {
            LogMessage("🔍 VERIFICANDO PRERREQUISITOS...");

            try
            {
                using var wpConnection = new MySqlConnection(_wpConnectionString);
                await wpConnection.OpenAsync();

                bool allGood = true;

                // Verificar usuarios
                var userCount = await wpConnection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM user_mapping");
                if (userCount == 0)
                {
                    LogMessage("⚠️ No hay usuarios migrados. Migra usuarios primero.");
                    allGood = false;
                }
                else
                {
                    LogMessage($"✅ Usuarios: {userCount} migrados");
                }

                // Verificar categorías (opcional)
                var categoryCount = await wpConnection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM category_mapping");
                LogMessage($"📂 Categorías: {categoryCount} migradas");

                // Verificar tags (opcional)
                var tagCount = await wpConnection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM tag_mapping");
                LogMessage($"🏷️ Tags: {tagCount} migrados");

                // Verificar conectividad a WordPress
                try
                {
                    var wpUsers = await _wpClient.Users.GetAllAsync();
                    LogMessage($"✅ Conectividad a WordPress: OK ({wpUsers.Count} usuarios en WP)");
                }
                catch (Exception ex)
                {
                    LogMessage($"❌ Error conectando a WordPress: {ex.Message}");
                    allGood = false;
                }

                // Verificar conectividad a Drupal
                using var drupalConnection = new MySqlConnection(_drupalConnectionString);
                try
                {
                    await drupalConnection.OpenAsync();
                    var drupalPostCount = await drupalConnection.QueryFirstOrDefaultAsync<int>(
                        "SELECT COUNT(*) FROM node WHERE type IN ('article', 'blog', 'story')");
                    LogMessage($"✅ Conectividad a Drupal: OK ({drupalPostCount} posts disponibles)");
                }
                catch (Exception ex)
                {
                    LogMessage($"❌ Error conectando a Drupal: {ex.Message}");
                    allGood = false;
                }

                if (allGood)
                {
                    LogMessage("✅ Todos los prerrequisitos están OK");
                }
                else
                {
                    LogMessage("❌ Hay prerrequisitos que no se cumplen");
                }

                return allGood;
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error verificando prerrequisitos: {ex.Message}");
                return false;
            }
        }

        public async Task ShowMigrationStatusAsync()
        {
            try
            {
                LogMessage("📊 ESTADO ACTUAL DE LA MIGRACIÓN:");

                using var wpConnection = new MySqlConnection(_wpConnectionString);
                await wpConnection.OpenAsync();

                // Estado de mappings
                var userCount = await wpConnection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM user_mapping");
                var categoryCount = await wpConnection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM category_mapping");
                var tagCount = await wpConnection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM tag_mapping");
                var postCount = await wpConnection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM post_mapping");
                var mediaCount = await wpConnection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM media_mapping");

                LogMessage($"   👥 Usuarios migrados: {userCount}");
                LogMessage($"   📂 Categorías migradas: {categoryCount}");
                LogMessage($"   🏷️ Tags migrados: {tagCount}");
                LogMessage($"   📝 Posts migrados: {postCount}");
                LogMessage($"   🖼️ Imágenes migradas: {mediaCount}");

                // Posts más recientes migrados
                if (postCount > 0)
                {
                    LogMessage("\n📋 Últimos 5 posts migrados:");
                    var recentPosts = await wpConnection.QueryAsync<dynamic>(@"
                        SELECT pm.drupal_post_id, pm.wp_post_id, pm.migrated_at,
                               wp.post_title
                        FROM post_mapping pm
                        LEFT JOIN wp_posts wp ON pm.wp_post_id = wp.ID
                        ORDER BY pm.migrated_at DESC
                        LIMIT 5");

                    foreach (var post in recentPosts)
                    {
                        LogMessage($"   - [{post.drupal_post_id}→{post.wp_post_id}] {post.post_title}");
                    }
                }

                // Estado de WordPress
                var wpPostCount = await wpConnection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM wp_posts WHERE post_type = 'post'");
                var wpImageCount = await wpConnection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM wp_posts WHERE post_type = 'attachment'");

                LogMessage($"\n📈 ESTADO DE WORDPRESS:");
                LogMessage($"   📝 Total posts en WP: {wpPostCount}");
                LogMessage($"   🖼️ Total imágenes en WP: {wpImageCount}");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error obteniendo estado: {ex.Message}");
            }
        }

        public async Task ValidateMigrationAsync()
        {
            LogMessage("🔍 VALIDANDO MIGRACIÓN...");

            try
            {
                using var wpConnection = new MySqlConnection(_wpConnectionString);
                await wpConnection.OpenAsync();

                // Verificar integridad de posts
                var orphanedPosts = await wpConnection.QueryAsync<dynamic>(@"
                    SELECT pm.drupal_post_id, pm.wp_post_id
                    FROM post_mapping pm
                    LEFT JOIN wp_posts wp ON pm.wp_post_id = wp.ID
                    WHERE wp.ID IS NULL");

                if (orphanedPosts.Any())
                {
                    LogMessage($"⚠️ Encontrados {orphanedPosts.Count()} posts huérfanos en mapping");
                }
                else
                {
                    LogMessage("✅ Todos los posts en mapping existen en WordPress");
                }

                // Verificar integridad de imágenes
                var orphanedImages = await wpConnection.QueryAsync<dynamic>(@"
                    SELECT mm.drupal_file_id, mm.wp_media_id
                    FROM media_mapping mm
                    LEFT JOIN wp_posts wp ON mm.wp_media_id = wp.ID
                    WHERE wp.ID IS NULL");

                if (orphanedImages.Any())
                {
                    LogMessage($"⚠️ Encontradas {orphanedImages.Count()} imágenes huérfanas en mapping");
                }
                else
                {
                    LogMessage("✅ Todas las imágenes en mapping existen en WordPress");
                }

                // Verificar posts sin autor válido
                var postsWithoutAuthor = await wpConnection.QueryAsync<dynamic>(@"
                    SELECT wp.ID, wp.post_title, wp.post_author
                    FROM wp_posts wp
                    LEFT JOIN wp_users wu ON wp.post_author = wu.ID
                    WHERE wp.post_type = 'post' 
                    AND wu.ID IS NULL");

                if (postsWithoutAuthor.Any())
                {
                    LogMessage($"⚠️ Encontrados {postsWithoutAuthor.Count()} posts sin autor válido");
                }
                else
                {
                    LogMessage("✅ Todos los posts tienen autores válidos");
                }

                LogMessage("✅ Validación completada");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error en validación: {ex.Message}");
            }
        }

        #endregion

        #region Métodos de Procesamiento de Contenido

        private async Task<string> ProcessContentAsync(string content)
        {
            if (string.IsNullOrEmpty(content))
                return "";

            // Procesar enlaces internos de Drupal
            content = await ProcessInternalLinksAsync(content);

            // Procesar imágenes embebidas
            content = await ProcessEmbeddedImagesAsync(content);

            return content;
        }

        private async Task<string> ProcessInternalLinksAsync(string content)
        {
            var nodePattern = @"href=[""']([^""']*node/(\d+)[^""']*)[""']";
            var matches = Regex.Matches(content, nodePattern);

            foreach (Match match in matches)
            {
                var nodeId = int.Parse(match.Groups[2].Value);
                if (_postMapping.ContainsKey(nodeId))
                {
                    var wpPostId = _postMapping[nodeId];
                    var wpPost = await _wpClient.Posts.GetByIDAsync(wpPostId);
                    content = content.Replace(match.Groups[1].Value, wpPost.Link);
                }
            }

            return content;
        }

        private async Task<string> ProcessEmbeddedImagesAsync(string content)
        {
            var imgPattern = @"<img[^>]+src=[""']([^""']+)[""'][^>]*>";
            var matches = Regex.Matches(content, imgPattern);

            foreach (Match match in matches)
            {
                var imgSrc = match.Groups[1].Value;
                if (imgSrc.Contains("sites/default/files"))
                {
                    try
                    {
                        var wpImageUrl = await MigrateImageJustInTimeAsync(imgSrc);
                        if (!string.IsNullOrEmpty(wpImageUrl))
                        {
                            content = content.Replace(imgSrc, wpImageUrl);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"⚠️ Error migrando imagen embebida {imgSrc}: {ex.Message}");
                    }
                }
            }

            return content;
        }

        #endregion

        #region Métodos de Migración de Imágenes

        private async Task<string> MigrateImageJustInTimeAsync(string drupalImagePath)
        {
            var cleanUrl = GetCleanImageUrl(drupalImagePath);

            if (_imageUrlCache.TryGetValue(cleanUrl, out int cachedWpId))
            {
                var cachedMedia = await _wpClient.Media.GetByIDAsync(cachedWpId);
                return cachedMedia.SourceUrl;
            }

            try
            {
                var imageUrl = drupalImagePath.StartsWith("http")
                    ? drupalImagePath
                    : $"http://localhost/drupal/{cleanUrl}";

                using var response = await _httpClient.GetAsync(imageUrl);
                response.EnsureSuccessStatusCode();

                using var imageStream = await response.Content.ReadAsStreamAsync();
                var fileName = Path.GetFileName(cleanUrl);

                var mediaItem = await _wpClient.Media.CreateAsync(imageStream, fileName);

                _imageUrlCache[cleanUrl] = mediaItem.Id;
                await SaveMediaMappingAsync(0, mediaItem.Id, fileName, $"public://{cleanUrl}", mediaItem.SourceUrl);

                LogMessage($"📷 Imagen migrada: {fileName}");
                return mediaItem.SourceUrl;
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Error migrando imagen {cleanUrl}: {ex.Message}");
                return null;
            }
        }

        private async Task SetFeaturedImageAsync(int postId, int drupalFid, string imageUri, string filename)
        {
            try
            {
                // Verificar cache primero
                if (_imageFidCache.TryGetValue(drupalFid, out int cachedWpId))
                {
                    var post = await _wpClient.Posts.GetByIDAsync(postId);
                    post.FeaturedMedia = cachedWpId;
                    await _wpClient.Posts.UpdateAsync(post);
                    return;
                }

                // Construir ruta completa de la imagen
                var imagePath = imageUri.Replace("public://", "sites/default/files/");
                var imageUrl = $"http://localhost/drupal/{imagePath}";

                // Descargar y subir imagen
                using var response = await _httpClient.GetAsync(imageUrl);
                response.EnsureSuccessStatusCode();

                using var imageStream = await response.Content.ReadAsStreamAsync();

                var mediaItem = await _wpClient.Media.CreateAsync(imageStream, filename);

                // Establecer como imagen destacada
                var postToUpdate = await _wpClient.Posts.GetByIDAsync(postId);
                postToUpdate.FeaturedMedia = mediaItem.Id;
                await _wpClient.Posts.UpdateAsync(postToUpdate);

                // Guardar en cache y BD
                _imageFidCache[drupalFid] = mediaItem.Id;
                await SaveMediaMappingAsync(drupalFid, mediaItem.Id, filename, imageUri, mediaItem.SourceUrl);

                LogMessage($"🖼️ Imagen destacada: {filename}");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Error con imagen destacada: {ex.Message}");
            }
        }

        private string GetCleanImageUrl(string imagePath)
        {
            if (imagePath.StartsWith("public://"))
            {
                return imagePath.Replace("public://", "sites/default/files/");
            }

            if (imagePath.Contains("sites/default/files/"))
            {
                var index = imagePath.IndexOf("sites/default/files/");
                return imagePath.Substring(index);
            }

            return imagePath;
        }

        #endregion

        #region Métodos de Consulta de Datos

        private async Task<List<int>> GetPostCategoriesAsync(MySqlConnection connection, int nodeId)
        {
            var query = @"
                SELECT tid 
                FROM field_data_field_categories 
                WHERE entity_id = @nodeId
                UNION
                SELECT tid 
                FROM field_data_panopoly_categories 
                WHERE entity_id = @nodeId
                UNION
                SELECT tid 
                FROM field_data_bibliteca_categorias 
                WHERE entity_id = @nodeId";

            var categories = await connection.QueryAsync<int>(query, new { nodeId });
            return categories.ToList();
        }

        private async Task<List<int>> GetPostTagsAsync(MySqlConnection connection, int nodeId)
        {
            var query = @"
                SELECT tid 
                FROM field_data_field_tags 
                WHERE entity_id = @nodeId";

            var tags = await connection.QueryAsync<int>(query, new { nodeId });
            return tags.ToList();
        }

        private async Task<List<MigratedPostInfo>> GetMigratedPostsAsync()
        {
            using var connection = new MySqlConnection(_wpConnectionString);
            await connection.OpenAsync();

            var posts = await connection.QueryAsync<MigratedPostInfo>(@"
                SELECT 
                    drupal_post_id as DrupalPostId,
                    wp_post_id as WpPostId,
                    migrated_at as MigratedAt
                FROM post_mapping
                ORDER BY migrated_at DESC");

            return posts.ToList();
        }

        #endregion

        #region Métodos de Análisis Detallado

        private async Task AnalyzeImageFieldsAsync(MySqlConnection connection)
        {
            LogMessage("\n🔍 Analizando campos de imagen:");

            // Verificar field_data_field_imagen (tabla antigua)
            var imagenCount = 0;
            try
            {
                imagenCount = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM field_data_field_imagen");
            }
            catch
            {
                // Tabla no existe
            }
            LogMessage($"   - field_data_field_imagen: {imagenCount} registros");

            // Verificar field_data_field_featured_image (tabla correcta)
            var featuredCount = 0;
            try
            {
                featuredCount = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM field_data_field_featured_image");
            }
            catch
            {
                // Tabla no existe
            }
            LogMessage($"   - field_data_field_featured_image: {featuredCount} registros");

            // Mostrar estructura de la tabla que usamos (field_data_field_featured_image)
            if (featuredCount > 0)
            {
                LogMessage("\n📋 Estructura de field_data_field_featured_image (TABLA QUE USAMOS):");
                try
                {
                    var featuredStructure = await connection.QueryAsync<dynamic>(@"
                        SELECT entity_id, field_featured_image_fid, field_featured_image_alt, field_featured_image_title 
                        FROM field_data_field_featured_image 
                        LIMIT 3");

                    foreach (var row in featuredStructure)
                    {
                        LogMessage($"   Ejemplo: entity_id={row.entity_id}, fid={row.field_featured_image_fid}");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"   Error leyendo estructura: {ex.Message}");
                }
            }
            else
            {
                LogMessage("   ⚠️ ADVERTENCIA: No se encontraron registros en field_data_field_featured_image");
                LogMessage("   ℹ️ Los posts no tendrán imágenes destacadas");
            }

            // Verificar si hay otras tablas de imagen
            LogMessage("\n🔍 Buscando otras tablas de imagen:");
            try
            {
                var imageTables = await connection.QueryAsync<dynamic>(@"
                    SELECT TABLE_NAME 
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_SCHEMA = DATABASE() 
                    AND (TABLE_NAME LIKE '%field%image%' 
                         OR TABLE_NAME LIKE '%field%foto%' 
                         OR TABLE_NAME LIKE '%field%picture%'
                         OR TABLE_NAME LIKE '%field%featured%')
                    ORDER BY TABLE_NAME");

                foreach (var table in imageTables)
                {
                    var count = await connection.QueryFirstOrDefaultAsync<int>($"SELECT COUNT(*) FROM {table.TABLE_NAME}");
                    LogMessage($"   - {table.TABLE_NAME}: {count} registros");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"   Error buscando tablas: {ex.Message}");
            }
        }

        private async Task AnalyzeSamplePostsAsync(MySqlConnection connection)
        {
            LogMessage("\n📝 Posts de ejemplo:");

            var samplePosts = await connection.QueryAsync<dynamic>(@"
                SELECT 
                    n.nid,
                    n.title,
                    n.type,
                    n.uid,
                    n.status,
                    CASE WHEN b.body_value IS NOT NULL THEN 'SÍ' ELSE 'NO' END as tiene_body,
                    CASE WHEN bj.field_bajada_value IS NOT NULL THEN 'SÍ' ELSE 'NO' END as tiene_bajada
                FROM node n
                LEFT JOIN field_data_body b ON n.nid = b.entity_id
                LEFT JOIN field_data_field_bajada bj ON n.nid = bj.entity_id
                WHERE n.type IN ('article', 'blog', 'story')
                ORDER BY n.created DESC
                LIMIT 5");

            foreach (var post in samplePosts)
            {
                LogMessage($"   📄 [{post.nid}] {post.title}");
                LogMessage($"      Tipo: {post.type}, Usuario: {post.uid}, Body: {post.tiene_body}, Bajada: {post.tiene_bajada}");
            }
        }

        #endregion

        #region Métodos de Guardado

        private async Task SaveMediaMappingAsync(int drupalFid, int wpId, string filename, string uri, string wpUrl)
        {
            using var wpConnection = new MySqlConnection(_wpConnectionString);
            await wpConnection.OpenAsync();

            await wpConnection.ExecuteAsync(@"
                INSERT INTO media_mapping (drupal_file_id, wp_media_id, drupal_filename, wp_filename, drupal_uri, wp_url, migrated_at) 
                VALUES (@drupalFid, @wpId, @drupalFilename, @wpFilename, @drupalUri, @wpUrl, @migratedAt)
                ON DUPLICATE KEY UPDATE 
                    wp_media_id = @wpId, 
                    wp_filename = @wpFilename, 
                    wp_url = @wpUrl,
                    migrated_at = @migratedAt",
                new
                {
                    drupalFid,
                    wpId,
                    drupalFilename = filename,
                    wpFilename = filename,
                    drupalUri = uri,
                    wpUrl,
                    migratedAt = DateTime.Now
                });
        }

        private async Task SavePostMappingAsync(int drupalId, int wpId)
        {
            using var wpConnection = new MySqlConnection(_wpConnectionString);
            await wpConnection.OpenAsync();

            await wpConnection.ExecuteAsync(
                "INSERT INTO post_mapping (drupal_post_id, wp_post_id, migrated_at) VALUES (@drupalId, @wpId, @migratedAt) ON DUPLICATE KEY UPDATE wp_post_id = @wpId, migrated_at = @migratedAt",
                new { drupalId, wpId, migratedAt = DateTime.Now });
        }

        private async Task CleanupPostMappingsAsync()
        {
            using var connection = new MySqlConnection(_wpConnectionString);
            await connection.OpenAsync();

            var deletedMappings = await connection.ExecuteAsync("DELETE FROM post_mapping");
            LogMessage($"🧹 Eliminados {deletedMappings} registros de mapping");
        }
        #endregion

        #region Métodos de Análisis

        public async Task AnalyzeDrupalPostsAsync()
        {
            LogMessage("🔍 ANALIZANDO ESTRUCTURA DE POSTS EN DRUPAL...");

            try
            {
                using var connection = new MySqlConnection(_drupalConnectionString);
                await connection.OpenAsync();

                // 1. Analizar tipos de contenido
                LogMessage("📊 Tipos de contenido:");
                var contentTypes = await connection.QueryAsync<dynamic>(@"
                    SELECT type, COUNT(*) as cantidad 
                    FROM node 
                    GROUP BY type 
                    ORDER BY cantidad DESC");

                foreach (var type in contentTypes)
                {
                    LogMessage($"   - {type.type}: {type.cantidad} nodos");
                }

                // 2. Verificar campos de imagen
                await AnalyzeImageFieldsAsync(connection);

                // 3. Analizar posts de ejemplo
                await AnalyzeSamplePostsAsync(connection);

                LogMessage("✅ Análisis completado.");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error en análisis: {ex.Message}");
                throw;
            }
        }

        public async Task<PostAnalysisResult> GetPostAnalysisAsync()
        {
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();

            var result = new PostAnalysisResult();

            LogMessage("📊 Calculando estadísticas de posts...");

            // Contar posts por tipo
            var postCounts = await connection.QueryAsync<dynamic>(@"
                SELECT type, COUNT(*) as count 
                FROM node 
                WHERE type IN ('article', 'blog', 'story')
                GROUP BY type");

            result.PostCountsByType = postCounts.ToDictionary(x => (string)x.type, x => (int)x.count);

            // Contar posts con imágenes (usando field_data_field_featured_image)
            try
            {
                result.PostsWithFeaturedImage = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT COUNT(DISTINCT n.nid)
                    FROM node n
                    JOIN field_data_field_featured_image img ON n.nid = img.entity_id
                    WHERE n.type IN ('article', 'blog', 'story')
                    AND img.field_featured_image_fid IS NOT NULL
                    AND img.field_featured_image_fid > 0");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Error contando posts con imagen: {ex.Message}");
                result.PostsWithFeaturedImage = 0;
            }

            // Contar posts con bajadas
            try
            {
                result.PostsWithBajada = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT COUNT(DISTINCT n.nid)
                    FROM node n
                    JOIN field_data_field_bajada bj ON n.nid = bj.entity_id
                    WHERE n.type IN ('article', 'blog', 'story')
                    AND bj.field_bajada_value IS NOT NULL
                    AND bj.field_bajada_value != ''");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Error contando posts con bajada: {ex.Message}");
                result.PostsWithBajada = 0;
            }

            // Total de posts
            result.TotalPosts = await connection.QueryFirstOrDefaultAsync<int>(@"
                SELECT COUNT(*) 
                FROM node 
                WHERE type IN ('article', 'blog', 'story')");

            LogMessage($"📈 Estadísticas calculadas:");
            LogMessage($"   Total posts: {result.TotalPosts}");
            LogMessage($"   Con imagen destacada: {result.PostsWithFeaturedImage}");
            LogMessage($"   Con bajada: {result.PostsWithBajada}");

            return result;
        }

        #endregion

        #region Métodos de Rollback

        public async Task RollbackPostMigrationAsync()
        {
            LogMessage("🔄 INICIANDO ROLLBACK DE POSTS...");

            try
            {
                var migratedPosts = await GetMigratedPostsAsync();
                LogMessage($"📊 Encontrados {migratedPosts.Count} posts migrados");

                if (migratedPosts.Count == 0)
                {
                    LogMessage("✅ No hay posts para eliminar");
                    return;
                }

                var result = MessageBox.Show(
                    $"¿Eliminar {migratedPosts.Count} posts migrados?\n\n⚠️ No se puede deshacer.",
                    "Confirmar Rollback", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    LogMessage("❌ Rollback cancelado");
                    return;
                }

                int deletedCount = 0;
                foreach (var post in migratedPosts)
                {
                    try
                    {
                        await _wpClient.Posts.DeleteAsync(post.WpPostId);
                        deletedCount++;

                        if (deletedCount % 10 == 0)
                        {
                            LogMessage($"🗑️ Eliminados: {deletedCount}/{migratedPosts.Count}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"⚠️ Error eliminando post {post.WpPostId}: {ex.Message}");
                    }
                }

                await CleanupPostMappingsAsync();
                LogMessage($"✅ Rollback completado: {deletedCount} posts eliminados");
            }
            catch (Exception ex)
            {
                LogMessage($"💥 Error en rollback: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Métodos Auxiliares

        private async Task LoadMappingsAsync()
        {
            LogMessage("📋 Cargando mappings...");

            using var wpConnection = new MySqlConnection(_wpConnectionString);
            await wpConnection.OpenAsync();

            var userMappings = await wpConnection.QueryAsync<dynamic>(
                "SELECT drupal_user_id, wp_user_id FROM user_mapping WHERE drupal_user_id IS NOT NULL");
            _userMapping = userMappings.ToDictionary(x => (int)x.drupal_user_id, x => (int)x.wp_user_id);

            var categoryMappings = await wpConnection.QueryAsync<dynamic>(
                "SELECT drupal_category_id, wp_category_id FROM category_mapping WHERE drupal_category_id IS NOT NULL");
            _categoryMapping = categoryMappings.ToDictionary(x => (int)x.drupal_category_id, x => (int)x.wp_category_id);

            var tagMappings = await wpConnection.QueryAsync<dynamic>(
                "SELECT drupal_tag_id, wp_tag_id FROM tag_mapping WHERE drupal_tag_id IS NOT NULL");
            _tagMapping = tagMappings.ToDictionary(x => (int)x.drupal_tag_id, x => (int)x.wp_tag_id);

            LogMessage($"✅ Mappings: Usuarios={_userMapping.Count}, Categorías={_categoryMapping.Count}, Tags={_tagMapping.Count}");
        }

        private async Task LoadImageCacheAsync()
        {
            LogMessage("📦 Cargando cache de imágenes...");

            using var wpConnection = new MySqlConnection(_wpConnectionString);
            await wpConnection.OpenAsync();

            var fidMappings = await wpConnection.QueryAsync<dynamic>(
                "SELECT drupal_file_id, wp_media_id FROM media_mapping");

            foreach (var mapping in fidMappings)
            {
                _imageFidCache[(int)mapping.drupal_file_id] = (int)mapping.wp_media_id;
            }

            LogMessage($"✅ Cache cargado: {_imageFidCache.Count} imágenes");
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}\n";

            _statusTextBlock.Dispatcher.Invoke(() =>
            {
                _statusTextBlock.Text += logEntry;
                _logScrollViewer?.ScrollToBottom();
            });
        }

        #endregion
    }

    #region Clases de Datos

    public class PostAnalysisResult
    {
        public Dictionary<string, int> PostCountsByType { get; set; } = new();
        public int TotalPosts { get; set; }
        public int PostsWithFeaturedImage { get; set; }
        public int PostsWithBajada { get; set; }
    }

    public class MigratedPostInfo
    {
        public int DrupalPostId { get; set; }
        public int WpPostId { get; set; }
        public DateTime MigratedAt { get; set; }
    }

    public class DrupalPost
    {
        public int Nid { get; set; }
        public string Title { get; set; }
        public int Uid { get; set; }
        public int Created { get; set; }
        public int Changed { get; set; }
        public int Status { get; set; }
        public string Content { get; set; }
        public string Excerpt { get; set; }
        public string Bajada { get; set; }
        public int? ImageFid { get; set; }
        public string ImageFilename { get; set; }
        public string ImageUri { get; set; }
        public List<int> Categories { get; set; } = new();
        public List<int> Tags { get; set; } = new();
    }

    #endregion
}