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

        public PostMigratorWPF(string drupalConnectionString, string wpConnectionString,
                              WordPressClient wpClient, TextBlock statusTextBlock, ScrollViewer logScrollViewer)
        {
            _drupalConnectionString = drupalConnectionString;
            _wpConnectionString = wpConnectionString;
            _wpClient = wpClient;
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer;
        }

        public async Task<Dictionary<int, int>> MigratePostsAsync()
        {
            LogMessage("Iniciando migración de posts...");

            try
            {
                // 1. Cargar mappings existentes
                await LoadMappingsAsync();

                // 2. Obtener posts de Drupal
                var drupalPosts = await GetDrupalPostsAsync();
                LogMessage($"Encontrados {drupalPosts.Count} posts en Drupal");

                // 3. Migrar cada post
                int migratedCount = 0;
                foreach (var post in drupalPosts)
                {
                    try
                    {
                        await MigratePostAsync(post);
                        migratedCount++;
                        LogMessage($"Migrado post: {post.Title} ({migratedCount}/{drupalPosts.Count})");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error migrando post {post.Title}: {ex.Message}");
                    }
                }

                LogMessage($"Migración completada: {migratedCount} posts migrados");
                return _postMapping;
            }
            catch (Exception ex)
            {
                LogMessage($"Error en migración: {ex.Message}");
                throw;
            }
        }

        private async Task LoadMappingsAsync()
        {
            LogMessage("Cargando mappings existentes...");

            using var wpConnection = new MySqlConnection(_wpConnectionString);
            await wpConnection.OpenAsync();

            // Cargar mapping de usuarios
            var userMappings = await wpConnection.QueryAsync<dynamic>(
                "SELECT drupal_id, wp_id FROM user_mapping WHERE drupal_id IS NOT NULL");
            _userMapping = userMappings.ToDictionary(x => (int)x.drupal_id, x => (int)x.wp_id);

            // Cargar mapping de categorías
            var categoryMappings = await wpConnection.QueryAsync<dynamic>(
                "SELECT drupal_id, wp_id FROM category_mapping WHERE drupal_id IS NOT NULL");
            _categoryMapping = categoryMappings.ToDictionary(x => (int)x.drupal_id, x => (int)x.wp_id);

            // Cargar mapping de tags
            var tagMappings = await wpConnection.QueryAsync<dynamic>(
                "SELECT drupal_id, wp_id FROM tag_mapping WHERE drupal_id IS NOT NULL");
            _tagMapping = tagMappings.ToDictionary(x => (int)x.drupal_id, x => (int)x.wp_id);

            LogMessage($"Mappings cargados - Usuarios: {_userMapping.Count}, Categorías: {_categoryMapping.Count}, Tags: {_tagMapping.Count}");
        }

        private async Task<List<DrupalPost>> GetDrupalPostsAsync()
        {
            using var drupalConnection = new MySqlConnection(_drupalConnectionString);
            await drupalConnection.OpenAsync();

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
                    img.field_imagen_fid AS image_fid,
                    f.filename AS image_filename,
                    f.uri AS image_uri
                FROM node n
                LEFT JOIN field_data_body b ON n.nid = b.entity_id
                LEFT JOIN field_data_field_bajada bj ON n.nid = bj.entity_id
                LEFT JOIN field_data_field_imagen img ON n.nid = img.entity_id
                LEFT JOIN file_managed f ON img.field_imagen_fid = f.fid
                WHERE n.type IN ('article', 'blog', 'story')
                ORDER BY n.created";

            var posts = await drupalConnection.QueryAsync<DrupalPost>(query);
            var postList = posts.ToList();

            // Obtener categorías y tags para cada post
            foreach (var post in postList)
            {
                post.Categories = await GetPostCategoriesAsync(drupalConnection, post.Nid);
                post.Tags = await GetPostTagsAsync(drupalConnection, post.Nid);
            }

            return postList;
        }

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
            if (!string.IsNullOrEmpty(drupalPost.ImageUri))
            {
                await SetFeaturedImageAsync(createdPost.Id, drupalPost.ImageUri, drupalPost.ImageFilename);
            }

            // Guardar mapping
            await SavePostMappingAsync(drupalPost.Nid, createdPost.Id);
            _postMapping[drupalPost.Nid] = createdPost.Id;
        }

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
            // Buscar enlaces internos de Drupal (node/123)
            var nodePattern = @"href=[""']([^""']*node/(\d+)[^""']*)[""']";
            var matches = Regex.Matches(content, nodePattern);

            foreach (Match match in matches)
            {
                var nodeId = int.Parse(match.Groups[2].Value);
                if (_postMapping.ContainsKey(nodeId))
                {
                    // Reemplazar con el enlace de WordPress
                    var wpPostId = _postMapping[nodeId];
                    var wpPost = await _wpClient.Posts.GetByIDAsync(wpPostId);
                    content = content.Replace(match.Groups[1].Value, wpPost.Link);
                }
            }

            return content;
        }

        private async Task<string> ProcessEmbeddedImagesAsync(string content)
        {
            // Buscar imágenes embebidas en el contenido
            var imgPattern = @"<img[^>]+src=[""']([^""']+)[""'][^>]*>";
            var matches = Regex.Matches(content, imgPattern);

            foreach (Match match in matches)
            {
                var imgSrc = match.Groups[1].Value;
                if (imgSrc.Contains("sites/default/files"))
                {
                    try
                    {
                        // Migrar imagen a WordPress
                        var wpImageUrl = await MigrateImageToWordPressAsync(imgSrc);
                        if (!string.IsNullOrEmpty(wpImageUrl))
                        {
                            content = content.Replace(imgSrc, wpImageUrl);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error migrando imagen {imgSrc}: {ex.Message}");
                    }
                }
            }

            return content;
        }

        private async Task<string> MigrateImageToWordPressAsync(string drupalImagePath)
        {
            try
            {
                // Construir URL completa de la imagen
                var imageUrl = drupalImagePath.StartsWith("http")
                    ? drupalImagePath
                    : $"http://localhost/drupal/{drupalImagePath.TrimStart('/')}";

                // Descargar imagen
                using var httpClient = new HttpClient();
                using var response = await httpClient.GetAsync(imageUrl);
                response.EnsureSuccessStatusCode();

                using var imageStream = await response.Content.ReadAsStreamAsync();
                var fileName = Path.GetFileName(drupalImagePath);

                // Subir a WordPress
                var mediaItem = await _wpClient.Media.CreateAsync(imageStream, fileName);
                return mediaItem.SourceUrl;
            }
            catch
            {
                return null;
            }
        }

        private async Task SetFeaturedImageAsync(int postId, string imageUri, string filename)
        {
            try
            {
                // Construir ruta completa de la imagen
                var imagePath = imageUri.Replace("public://", "sites/default/files/");
                var imageUrl = $"http://localhost/drupal/{imagePath}";

                // Descargar y subir imagen
                using var httpClient = new HttpClient();
                using var response = await httpClient.GetAsync(imageUrl);
                response.EnsureSuccessStatusCode();

                using var imageStream = await response.Content.ReadAsStreamAsync();

                var mediaItem = await _wpClient.Media.CreateAsync(imageStream, filename);

                // Establecer como imagen destacada
                var post = await _wpClient.Posts.GetByIDAsync(postId);
                post.FeaturedMedia = mediaItem.Id;
                await _wpClient.Posts.UpdateAsync(post);
            }
            catch (Exception ex)
            {
                LogMessage($"Error estableciendo imagen destacada: {ex.Message}");
            }
        }

        private async Task SavePostMappingAsync(int drupalId, int wpId)
        {
            using var wpConnection = new MySqlConnection(_wpConnectionString);
            await wpConnection.OpenAsync();

            await wpConnection.ExecuteAsync(
                "INSERT INTO post_mapping (drupal_id, wp_id) VALUES (@drupalId, @wpId) ON DUPLICATE KEY UPDATE wp_id = @wpId",
                new { drupalId, wpId });
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
}