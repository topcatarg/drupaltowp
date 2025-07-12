using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Models;

namespace drupaltowp.Clases.Publicaciones.Opinion;

/// <summary>
/// Gonzalo Bianchi - Limpiador seguro de publicaciones Opinion
/// Usa la API de WordPress para borrar todo de forma segura
/// </summary>
public class OpinionPostCleaner
{
    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly string _connectionString;
    public bool Cancelar { get; set; } = false;

    public OpinionPostCleaner(LoggerViewModel logger, WordPressClient wpClient)
    {
        _logger = logger;
        _wpClient = wpClient;
        _connectionString = ConfiguracionGeneral.WPconnectionString;
    }

    /// <summary>
    /// Limpia todas las publicaciones Opinion migradas usando WordPress API
    /// </summary>
    public async Task LimpiarPublicacionesMigradasAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogProcess("🧹 Iniciando limpieza de publicaciones Opinion migradas...");

            // 1. Obtener lista de posts migrados
            var postsMigrados = await GetPostsMigradosAsync();

            if (!postsMigrados.Any())
            {
                _logger.LogInfo("ℹ️ No se encontraron publicaciones Opinion para limpiar");
                return;
            }

            _logger.LogInfo($"📄 Se encontraron {postsMigrados.Count} publicaciones para limpiar");

            // 2. Confirmar que son posts de Opinion
            var postsConfirmados = await ConfirmarPostsOpinionAsync(postsMigrados);

            if (!postsConfirmados.Any())
            {
                _logger.LogWarning("⚠️ No se confirmaron posts de Opinion para borrar");
                return;
            }

            _logger.LogInfo($"✅ Confirmados {postsConfirmados.Count} posts de Opinion para borrar");

            // 3. Borrar posts usando WordPress API
            int borradosCount = 0;
            int errorCount = 0;

            foreach (var post in postsConfirmados)
            {
                if (Cancelar || cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("⚠️ Limpieza cancelada por el usuario");
                    break;
                }

                try
                {
                    var success = await BorrarPostWordPressAsync(post.WpPostId, post.PostTitle);

                    if (success)
                    {
                        borradosCount++;
                        _logger.LogInfo($"🗑️ Borrado: [{post.WpPostId}] {post.PostTitle}");
                    }
                    else
                    {
                        errorCount++;
                        _logger.LogError($"❌ Error borrando: [{post.WpPostId}] {post.PostTitle}");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _logger.LogError($"❌ Error borrando post [{post.WpPostId}]: {ex.Message}");
                }

                // Pausa pequeña para no sobrecargar WordPress
                await Task.Delay(200, cancellationToken);
            }

            // 4. Limpiar tabla de mapping
            if (borradosCount > 0)
            {
                await LimpiarMappingAsync();
            }

            // 5. Limpiar taxonomías huérfanas (opcional)
            await LimpiarTaxonomiasHuerfanasAsync();

            // 6. Resumen final
            _logger.LogSuccess("✅ Limpieza de publicaciones Opinion completada");
            _logger.LogInfo($"📊 Resumen:");
            _logger.LogInfo($"   🗑️ Posts borrados: {borradosCount}");
            _logger.LogInfo($"   ❌ Errores: {errorCount}");

            if (borradosCount > 0)
            {
                _logger.LogInfo("🔄 Sistema listo para migración limpia");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error en limpieza de publicaciones: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Obtiene la lista de posts migrados desde post_mapping_opinion
    /// </summary>
    private async Task<List<OpinionMappedPost>> GetPostsMigradosAsync()
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string query = @"
                SELECT 
                    pmo.drupal_post_id as DrupalPostId,
                    pmo.wp_post_id as WpPostId,
                    p.post_title as PostTitle,
                    p.post_type as PostType,
                    p.post_status as PostStatus
                FROM post_mapping_opinion pmo
                INNER JOIN wp_posts p ON pmo.wp_post_id = p.ID
                ORDER BY pmo.wp_post_id";

        var posts = await connection.QueryAsync<OpinionMappedPost>(query);
        return posts.ToList();
    }

    /// <summary>
    /// Confirma que los posts son realmente de Opinion antes de borrar
    /// </summary>
    private async Task<List<OpinionMappedPost>> ConfirmarPostsOpinionAsync(List<OpinionMappedPost> posts)
    {
        var postsConfirmados = new List<OpinionMappedPost>();

        foreach (var post in posts)
        {
            try
            {
                // Verificar directamente en la base de datos ya que WordPress API tiene limitaciones con custom post types
                var postExiste = await VerificarPostExisteAsync(post.WpPostId);

                if (postExiste)
                {
                    // Verificar si tiene metadatos de Opinion o es tipo opinion
                    var esOpinion = await EsPostOpinionAsync(post.WpPostId);

                    if (esOpinion)
                    {
                        postsConfirmados.Add(post);
                        _logger.LogInfo($"✅ Confirmado post Opinion: [{post.WpPostId}] {post.PostTitle}");
                    }
                    else
                    {
                        _logger.LogWarning($"⚠️ Post [{post.WpPostId}] no parece ser de Opinion, omitiendo");
                    }
                }
                else
                {
                    _logger.LogWarning($"⚠️ Post [{post.WpPostId}] no encontrado en WordPress");
                    // Aunque no exista, marcarlo para limpiar del mapping
                    postsConfirmados.Add(post);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ Error verificando post [{post.WpPostId}]: {ex.Message}");
            }
        }

        return postsConfirmados;
    }

    /// <summary>
    /// Verifica si un post existe en WordPress (cualquier tipo)
    /// </summary>
    private async Task<bool> VerificarPostExisteAsync(int postId)
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var existe = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT COUNT(*) 
                    FROM wp_posts 
                    WHERE ID = @PostId 
                    AND post_status IN ('publish', 'draft', 'private', 'trash')",
                new { PostId = postId });

            return existe > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifica si un post es de tipo Opinion o tiene metadatos de Opinion
    /// </summary>
    private async Task<bool> EsPostOpinionAsync(int postId)
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // Verificar si es tipo 'opinion' o tiene metadatos específicos
            var esOpinion = await connection.QueryFirstOrDefaultAsync<bool>(@"
                    SELECT 
                        CASE 
                            WHEN p.post_type = 'opinion' THEN 1
                            WHEN EXISTS (
                                SELECT 1 FROM wp_postmeta pm 
                                WHERE pm.post_id = p.ID 
                                AND pm.meta_key IN ('_mh_opinion_quote', '_mh_custom_author_name', '_mh_post_type')
                                AND pm.meta_value IS NOT NULL 
                                AND pm.meta_value != ''
                            ) THEN 1
                            ELSE 0
                        END as es_opinion
                    FROM wp_posts p
                    WHERE p.ID = @PostId",
                new { PostId = postId });

            return esOpinion;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Borra un post usando la API de WordPress (con conversión temporal si es necesario)
    /// </summary>
    private async Task<bool> BorrarPostWordPressAsync(int postId, string titulo)
    {
        try
        {
            // 1. Convertir temporalmente a tipo 'post' para que funcione con WordPress API
            var originalType = await ConvertirTemporalmenteAPostAsync(postId);

            if (string.IsNullOrEmpty(originalType))
            {
                _logger.LogWarning($"⚠️ No se pudo obtener tipo original del post {postId}");
                return await BorrarPostDirectoBDAsync(postId, titulo);
            }

            // 2. Intentar borrar con WordPress API (ahora debería funcionar)
            var success = await _wpClient.Posts.DeleteAsync(postId);

            if (success)
            {
                _logger.LogInfo($"✅ Post {postId} borrado exitosamente con WordPress API");
                return true;
            }
            else
            {
                _logger.LogWarning($"⚠️ WordPress API no pudo borrar post {postId}");
                // Si falla, restaurar tipo original y usar borrado directo
                await RestaurarTipoOriginalAsync(postId, originalType);
                return await BorrarPostDirectoBDAsync(postId, titulo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ Error con WordPress API para post {postId}: {ex.Message}");
            // Como backup, usar borrado directo en BD
            return await BorrarPostDirectoBDAsync(postId, titulo);
        }
    }

    /// <summary>
    /// Convierte temporalmente un post a tipo 'post' para usar con WordPress API
    /// </summary>
    private async Task<string> ConvertirTemporalmenteAPostAsync(int postId)
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // Obtener el tipo original
            var originalType = await connection.QueryFirstOrDefaultAsync<string>(@"
                    SELECT post_type FROM wp_posts WHERE ID = @PostId",
                new { PostId = postId });

            if (string.IsNullOrEmpty(originalType))
            {
                return null;
            }

            // Si ya es tipo 'post', no hacer nada
            if (originalType == "post")
            {
                return originalType;
            }

            // Convertir temporalmente a 'post'
            await connection.ExecuteAsync(@"
                    UPDATE wp_posts 
                    SET post_type = 'post' 
                    WHERE ID = @PostId",
                new { PostId = postId });

            _logger.LogInfo($"🔄 Post {postId} convertido temporalmente de '{originalType}' a 'post'");
            return originalType;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ Error convirtiendo post {postId} temporalmente: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Restaura el tipo original del post si el borrado falla
    /// </summary>
    private async Task RestaurarTipoOriginalAsync(int postId, string originalType)
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(@"
                    UPDATE wp_posts 
                    SET post_type = @OriginalType 
                    WHERE ID = @PostId",
                new { PostId = postId, OriginalType = originalType });

            _logger.LogInfo($"🔄 Post {postId} restaurado a tipo '{originalType}'");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ Error restaurando tipo original del post {postId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Borrado directo en BD como último recurso
    /// </summary>
    private async Task<bool> BorrarPostDirectoBDAsync(int postId, string titulo)
    {
        try
        {
            _logger.LogWarning($"⚠️ Intentando borrado directo en BD para post {postId}");

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // Borrar metadatos
            await connection.ExecuteAsync(@"
                    DELETE FROM wp_postmeta WHERE post_id = @PostId",
                new { PostId = postId });

            // Borrar relaciones con taxonomías
            await connection.ExecuteAsync(@"
                    DELETE FROM wp_term_relationships WHERE object_id = @PostId",
                new { PostId = postId });

            // Borrar comentarios
            await connection.ExecuteAsync(@"
                    DELETE FROM wp_comments WHERE comment_post_ID = @PostId",
                new { PostId = postId });

            // Borrar el post
            var filasAfectadas = await connection.ExecuteAsync(@"
                    DELETE FROM wp_posts WHERE ID = @PostId",
                new { PostId = postId });

            return filasAfectadas > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error en borrado directo BD para post {postId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Limpia la tabla de mapping después del borrado
    /// </summary>
    private async Task LimpiarMappingAsync()
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var filasAfectadas = await connection.ExecuteAsync(@"
                    DELETE FROM post_mapping_opinion");

            _logger.LogInfo($"🧹 Limpiada tabla post_mapping_opinion: {filasAfectadas} registros");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ Error limpiando mapping: {ex.Message}");
        }
    }

    /// <summary>
    /// Limpia taxonomías huérfanas (términos sin posts)
    /// </summary>
    private async Task LimpiarTaxonomiasHuerfanasAsync()
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // Borrar términos en categoria_opinion sin posts
            var categoriasBorradas = await connection.ExecuteAsync(@"
                    DELETE tt FROM wp_term_taxonomy tt
                    LEFT JOIN wp_term_relationships tr ON tt.term_taxonomy_id = tr.term_taxonomy_id
                    WHERE tt.taxonomy = 'categoria_opinion' 
                    AND tr.term_taxonomy_id IS NULL");

            // Borrar términos en tag_opinion sin posts
            var tagsBorrados = await connection.ExecuteAsync(@"
                    DELETE tt FROM wp_term_taxonomy tt
                    LEFT JOIN wp_term_relationships tr ON tt.term_taxonomy_id = tr.term_taxonomy_id
                    WHERE tt.taxonomy = 'tag_opinion' 
                    AND tr.term_taxonomy_id IS NULL");

            // Borrar términos huérfanos (sin taxonomy)
            var terminosHuerfanos = await connection.ExecuteAsync(@"
                    DELETE t FROM wp_terms t
                    LEFT JOIN wp_term_taxonomy tt ON t.term_id = tt.term_id
                    WHERE tt.term_id IS NULL");

            if (categoriasBorradas > 0 || tagsBorrados > 0 || terminosHuerfanos > 0)
            {
                _logger.LogInfo($"🧹 Taxonomías limpiadas - Categorías: {categoriasBorradas}, Tags: {tagsBorrados}, Términos huérfanos: {terminosHuerfanos}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"⚠️ Error limpiando taxonomías: {ex.Message}");
        }
    }
}

// Modelo para posts migrados