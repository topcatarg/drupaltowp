using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace drupaltowp.Clases.Publicaciones.Opinion
{
    /// <summary>
    /// Gonzalo Bianchi - Corrector de tipos y contenido para posts de Opinion
    /// Recupera contenido original de Drupal y migra correctamente categorías/tags
    /// </summary>
    public class OpinionPostTypeCorrector
    {
        private readonly LoggerViewModel _logger;
        private readonly string _wpConnectionString;
        private readonly string _drupalConnectionString;
        public bool Cancelar { get; set; } = false;

        public OpinionPostTypeCorrector(LoggerViewModel logger)
        {
            _logger = logger;
            _wpConnectionString = ConfiguracionGeneral.WPconnectionString;
            _drupalConnectionString = ConfiguracionGeneral.DrupalconnectionString;
        }

        /// <summary>
        /// Corrige tipos de publicación y contenido
        /// </summary>
        public async Task CorrectPostTypesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogProcess("🔍 Iniciando corrección completa de posts Opinion...");

                using var wpConnection = new MySqlConnection(_wpConnectionString);
                using var drupalConnection = new MySqlConnection(_drupalConnectionString);

                await wpConnection.OpenAsync(cancellationToken);
                await drupalConnection.OpenAsync(cancellationToken);

                // 1. Obtener posts migrados desde post_mapping_opinion
                var opinionPosts = await GetMigratedOpinionPostsAsync(wpConnection);

                if (!opinionPosts.Any())
                {
                    _logger.LogInfo("ℹ️ No se encontraron posts de Opinion migrados");
                    return;
                }

                _logger.LogInfo($"📄 Procesando {opinionPosts.Count} posts de Opinion");

                // 2. Procesar cada post
                int processedCount = 0;
                int errorCount = 0;

                foreach (var post in opinionPosts)
                {
                    if (Cancelar || cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("⚠️ Operación cancelada por el usuario");
                        break;
                    }

                    try
                    {
                        await ProcessOpinionPostAsync(wpConnection, drupalConnection, post);
                        processedCount++;
                        _logger.LogInfo($"✅ Procesado: [{post.WpPostId}] {post.PostTitle}");
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logger.LogError($"❌ Error procesando post [{post.WpPostId}]: {ex.Message}");
                    }

                    await Task.Delay(100, cancellationToken);
                }

                // 3. Resumen final
                _logger.LogSuccess("✅ Corrección de posts Opinion completada");
                _logger.LogInfo($"📊 Resumen:");
                _logger.LogInfo($"   ✅ Posts procesados: {processedCount}");
                _logger.LogInfo($"   ❌ Errores: {errorCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error en corrección de Opinion: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Obtiene posts migrados usando post_mapping_opinion
        /// </summary>
        private async Task<List<OpinionMappedPost>> GetMigratedOpinionPostsAsync(MySqlConnection wpConnection)
        {
            const string query = @"
                SELECT 
                    pmo.drupal_post_id as DrupalPostId,
                    pmo.wp_post_id as WpPostId,
                    p.post_title as PostTitle,
                    p.post_type as PostType,
                    p.post_status as PostStatus
                FROM post_mapping_opinion pmo
                INNER JOIN wp_posts p ON pmo.wp_post_id = p.ID
                WHERE p.post_status IN ('publish', 'draft', 'private')
                ORDER BY pmo.wp_post_id";

            var posts = await wpConnection.QueryAsync<OpinionMappedPost>(query);
            return posts.ToList();
        }

        /// <summary>
        /// Procesa un post individual de Opinion
        /// </summary>
        private async Task ProcessOpinionPostAsync(MySqlConnection wpConnection, MySqlConnection drupalConnection, OpinionMappedPost post)
        {
            // 1. Obtener contenido original de Drupal
            var originalContent = await GetOriginalDrupalContentAsync(drupalConnection, post.DrupalPostId);

            if (string.IsNullOrEmpty(originalContent))
            {
                _logger.LogWarning($"⚠️ No se encontró contenido original para post Drupal {post.DrupalPostId}");
                originalContent = ""; // Usar contenido vacío si no se encuentra
            }

            // 2. Cambiar tipo de post a 'opinion' y actualizar contenido
            await wpConnection.ExecuteAsync(@"
                UPDATE wp_posts 
                SET post_type = 'opinion',
                    post_content = @Content
                WHERE ID = @PostId",
                new { PostId = post.WpPostId, Content = originalContent });

            // 3. Obtener y migrar categorías
            var categories = await GetPostCategoriesAsync(wpConnection, post.WpPostId);
            await MigrateCategoriesToOpinionAsync(wpConnection, post.WpPostId, categories);

            // 4. Obtener y migrar tags
            var tags = await GetPostTagsAsync(wpConnection, post.WpPostId);
            await MigrateTagsToOpinionAsync(wpConnection, post.WpPostId, tags);

            // 5. Remover categoría "Opinion" original
            await RemoveOpinionCategoryAsync(wpConnection, post.WpPostId);
        }

        /// <summary>
        /// Obtiene el contenido original de Drupal
        /// </summary>
        private async Task<string> GetOriginalDrupalContentAsync(MySqlConnection drupalConnection, int drupalPostId)
        {
            const string query = @"
                SELECT fdb.body_value
                FROM node n
                LEFT JOIN field_data_body fdb ON fdb.entity_id = n.nid
                WHERE n.nid = @DrupalPostId
                AND n.type = 'opinion'";

            var content = await drupalConnection.QueryFirstOrDefaultAsync<string>(query,
                new { DrupalPostId = drupalPostId });

            return content ?? "";
        }

        /// <summary>
        /// Obtiene las categorías actuales de un post
        /// </summary>
        private async Task<List<PostCategory>> GetPostCategoriesAsync(MySqlConnection wpConnection, int postId)
        {
            const string query = @"
                SELECT DISTINCT 
                    t.term_id as TermId,
                    t.name as Name,
                    t.slug as Slug
                FROM wp_term_relationships tr
                INNER JOIN wp_term_taxonomy tt ON tr.term_taxonomy_id = tt.term_taxonomy_id
                INNER JOIN wp_terms t ON tt.term_id = t.term_id
                WHERE tr.object_id = @PostId 
                AND tt.taxonomy = 'category'
                AND t.name NOT IN ('Opinion', 'Opinión', 'opinion', 'opinión')";

            var categories = await wpConnection.QueryAsync<PostCategory>(query, new { PostId = postId });
            return categories.ToList();
        }

        /// <summary>
        /// Obtiene los tags actuales de un post
        /// </summary>
        private async Task<List<PostTag>> GetPostTagsAsync(MySqlConnection wpConnection, int postId)
        {
            const string query = @"
                SELECT DISTINCT 
                    t.term_id as TermId,
                    t.name as Name,
                    t.slug as Slug
                FROM wp_term_relationships tr
                INNER JOIN wp_term_taxonomy tt ON tr.term_taxonomy_id = tt.term_taxonomy_id
                INNER JOIN wp_terms t ON tt.term_id = t.term_id
                WHERE tr.object_id = @PostId 
                AND tt.taxonomy = 'post_tag'";

            var tags = await wpConnection.QueryAsync<PostTag>(query, new { PostId = postId });
            return tags.ToList();
        }

        /// <summary>
        /// Migra categorías a categoria_opinion
        /// </summary>
        private async Task MigrateCategoriesToOpinionAsync(MySqlConnection wpConnection, int postId, List<PostCategory> categories)
        {
            if (!categories.Any())
            {
                _logger.LogInfo($"   📁 No hay categorías para migrar en post {postId}");
                return;
            }

            // Verificar si existe la taxonomía categoria_opinion
            var taxonomyExists = await wpConnection.QueryFirstOrDefaultAsync<int>(@"
                SELECT COUNT(*) 
                FROM wp_term_taxonomy 
                WHERE taxonomy = 'categoria_opinion'") > 0;

            if (!taxonomyExists)
            {
                _logger.LogWarning("⚠️ Taxonomía 'categoria_opinion' no existe. Creando categorías...");
            }

            foreach (var category in categories)
            {
                try
                {
                    var opinionCategoryId = await GetOrCreateOpinionCategoryAsync(wpConnection, category.Name, category.Slug);

                    if (opinionCategoryId > 0)
                    {
                        // Asociar el post con la nueva categoría
                        await wpConnection.ExecuteAsync(@"
                            INSERT IGNORE INTO wp_term_relationships (object_id, term_taxonomy_id)
                            VALUES (@PostId, @TaxonomyId)",
                            new { PostId = postId, TaxonomyId = opinionCategoryId });

                        _logger.LogInfo($"   📁 Migrada categoría '{category.Name}' a categoria_opinion");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"⚠️ Error migrando categoría '{category.Name}': {ex.Message}");
                }
            }

            // Remover categorías normales del post
            await wpConnection.ExecuteAsync(@"
                DELETE tr FROM wp_term_relationships tr
                INNER JOIN wp_term_taxonomy tt ON tr.term_taxonomy_id = tt.term_taxonomy_id
                WHERE tr.object_id = @PostId 
                AND tt.taxonomy = 'category'",
                new { PostId = postId });
        }

        /// <summary>
        /// Migra tags a tag_opinion
        /// </summary>
        private async Task MigrateTagsToOpinionAsync(MySqlConnection wpConnection, int postId, List<PostTag> tags)
        {
            if (!tags.Any())
            {
                _logger.LogInfo($"   🏷️ No hay tags para migrar en post {postId}");
                return;
            }

            // Verificar si existe la taxonomía tag_opinion
            var taxonomyExists = await wpConnection.QueryFirstOrDefaultAsync<int>(@"
                SELECT COUNT(*) 
                FROM wp_term_taxonomy 
                WHERE taxonomy = 'tag_opinion'") > 0;

            if (!taxonomyExists)
            {
                _logger.LogWarning("⚠️ Taxonomía 'tag_opinion' no existe. Creando tags...");
            }

            foreach (var tag in tags)
            {
                try
                {
                    var opinionTagId = await GetOrCreateOpinionTagAsync(wpConnection, tag.Name, tag.Slug);

                    if (opinionTagId > 0)
                    {
                        // Asociar el post con el nuevo tag
                        await wpConnection.ExecuteAsync(@"
                            INSERT IGNORE INTO wp_term_relationships (object_id, term_taxonomy_id)
                            VALUES (@PostId, @TaxonomyId)",
                            new { PostId = postId, TaxonomyId = opinionTagId });

                        _logger.LogInfo($"   🏷️ Migrado tag '{tag.Name}' a tag_opinion");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"⚠️ Error migrando tag '{tag.Name}': {ex.Message}");
                }
            }

            // Remover tags normales del post
            await wpConnection.ExecuteAsync(@"
                DELETE tr FROM wp_term_relationships tr
                INNER JOIN wp_term_taxonomy tt ON tr.term_taxonomy_id = tt.term_taxonomy_id
                WHERE tr.object_id = @PostId 
                AND tt.taxonomy = 'post_tag'",
                new { PostId = postId });
        }

        /// <summary>
        /// Remueve la categoría Opinion del post
        /// </summary>
        private async Task RemoveOpinionCategoryAsync(MySqlConnection wpConnection, int postId)
        {
            const string query = @"
                DELETE tr FROM wp_term_relationships tr
                INNER JOIN wp_term_taxonomy tt ON tr.term_taxonomy_id = tt.term_taxonomy_id
                INNER JOIN wp_terms t ON tt.term_id = t.term_id
                WHERE tr.object_id = @PostId 
                AND tt.taxonomy = 'category'
                AND t.name IN ('Opinion', 'Opinión', 'opinion', 'opinión')";

            var removedCount = await wpConnection.ExecuteAsync(query, new { PostId = postId });

            if (removedCount > 0)
            {
                _logger.LogInfo($"   🗑️ Removida categoría Opinion del post {postId}");
            }
        }

        /// <summary>
        /// Obtiene o crea una categoría en categoria_opinion
        /// </summary>
        private async Task<int> GetOrCreateOpinionCategoryAsync(MySqlConnection wpConnection, string name, string slug)
        {
            // Buscar si ya existe
            var existingId = await wpConnection.QueryFirstOrDefaultAsync<int?>(@"
                SELECT tt.term_taxonomy_id
                FROM wp_terms t
                INNER JOIN wp_term_taxonomy tt ON t.term_id = tt.term_id
                WHERE t.name = @Name AND tt.taxonomy = 'categoria_opinion'",
                new { Name = name });

            if (existingId.HasValue)
                return existingId.Value;

            // Crear nuevo término
            try
            {
                // Insertar en wp_terms
                await wpConnection.ExecuteAsync(@"
                    INSERT INTO wp_terms (name, slug, term_group) 
                    VALUES (@Name, @Slug, 0)",
                    new { Name = name, Slug = slug });

                // Obtener el ID del término recién creado
                var termId = await wpConnection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT term_id FROM wp_terms WHERE name = @Name AND slug = @Slug",
                    new { Name = name, Slug = slug });

                // Insertar en wp_term_taxonomy
                await wpConnection.ExecuteAsync(@"
                    INSERT INTO wp_term_taxonomy (term_id, taxonomy, description, parent, count) 
                    VALUES (@TermId, 'categoria_opinion', '', 0, 0)",
                    new { TermId = termId });

                // Obtener y retornar el term_taxonomy_id
                var taxonomyId = await wpConnection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT term_taxonomy_id FROM wp_term_taxonomy 
                    WHERE term_id = @TermId AND taxonomy = 'categoria_opinion'",
                    new { TermId = termId });

                return taxonomyId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error creando categoría opinion '{name}': {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Obtiene o crea un tag en tag_opinion
        /// </summary>
        private async Task<int> GetOrCreateOpinionTagAsync(MySqlConnection wpConnection, string name, string slug)
        {
            // Buscar si ya existe
            var existingId = await wpConnection.QueryFirstOrDefaultAsync<int?>(@"
                SELECT tt.term_taxonomy_id
                FROM wp_terms t
                INNER JOIN wp_term_taxonomy tt ON t.term_id = tt.term_id
                WHERE t.name = @Name AND tt.taxonomy = 'tag_opinion'",
                new { Name = name });

            if (existingId.HasValue)
                return existingId.Value;

            // Crear nuevo término
            try
            {
                // Insertar en wp_terms
                await wpConnection.ExecuteAsync(@"
                    INSERT INTO wp_terms (name, slug, term_group) 
                    VALUES (@Name, @Slug, 0)",
                    new { Name = name, Slug = slug });

                // Obtener el ID del término recién creado
                var termId = await wpConnection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT term_id FROM wp_terms WHERE name = @Name AND slug = @Slug",
                    new { Name = name, Slug = slug });

                // Insertar en wp_term_taxonomy
                await wpConnection.ExecuteAsync(@"
                    INSERT INTO wp_term_taxonomy (term_id, taxonomy, description, parent, count) 
                    VALUES (@TermId, 'tag_opinion', '', 0, 0)",
                    new { TermId = termId });

                // Obtener y retornar el term_taxonomy_id
                var taxonomyId = await wpConnection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT term_taxonomy_id FROM wp_term_taxonomy 
                    WHERE term_id = @TermId AND taxonomy = 'tag_opinion'",
                    new { TermId = termId });

                return taxonomyId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error creando tag opinion '{name}': {ex.Message}");
                return 0;
            }
        }
    }

    // Modelos para evitar problemas con dynamic
    public class OpinionMappedPost
    {
        public int DrupalPostId { get; set; }
        public int WpPostId { get; set; }
        public string PostTitle { get; set; }
        public string PostType { get; set; }
        public string PostStatus { get; set; }
    }

    public class PostCategory
    {
        public int TermId { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
    }

    public class PostTag
    {
        public int TermId { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
    }
}