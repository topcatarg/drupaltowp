using drupaltowp.Configuracion;
using drupaltowp.Models;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Models;

namespace drupaltowp.Clases.Publicaciones.Panopoly;

internal class MigratePanopolyPost
{
    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly MappingService _mappingService;

    public MigratePanopolyPost(LoggerViewModel logger, WordPressClient wpClient, MappingService mappingService)
    {
        _logger = logger;
        _wpClient = wpClient;
        _mappingService = mappingService;
    }

    /// <summary>
    /// Migra una página panopoly completa a WordPress
    /// </summary>
    public async Task<MigratedPostInfo> MigratePostAsync(PanopolyPage panopolyPage)
    {
        try
        {
            _logger.LogProcess($"Migrando página: [{panopolyPage.Nid}] {panopolyPage.Title}");

            // Verificar si ya está migrada
            if (_mappingService.IsPostMigrated(panopolyPage.Nid, ContentType.PanopolyPage))
            {
                var existingId = _mappingService.GetWordPressPostId(panopolyPage.Nid, ContentType.PanopolyPage);
                _logger.LogInfo($"   Ya migrada - WordPress ID: {existingId}");

                return new MigratedPostInfo
                {
                    DrupalPostId = panopolyPage.Nid,
                    WpPostId = existingId.Value,
                    MigratedAt = DateTime.Now
                };
            }

            // PASO 1: Migrar contenido básico
            var wpPost = await MigrateBasicContentAsync(panopolyPage);

            // PASO 2: Asignar categorías y tags
            AssignCategoriesAndTags(wpPost, panopolyPage);

            // Crear el post en WordPress
            var createdPost = await _wpClient.Posts.CreateAsync(wpPost);

            _logger.LogSuccess($"   Post creado - WordPress ID: {createdPost.Id}");

            // TODO: PRÓXIMOS PASOS
            // - Procesar imágenes

            // Guardar mapeo
            var mappingInfo = await SavePostMappingAsync(panopolyPage.Nid, createdPost.Id);

            _logger.LogSuccess($"   Migración completada: {panopolyPage.Title}");

            return mappingInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError($"   Error migrando [{panopolyPage.Nid}] {panopolyPage.Title}: {ex.Message}");
            throw;
        }
    }
    #region MIGRACIÓN DE CONTENIDO BÁSICO

    /// <summary>
    /// Migra el contenido básico: título, contenido con volanta, bajada, autor, fechas
    /// </summary>
    private async Task<Post> MigrateBasicContentAsync(PanopolyPage panopolyPage)
    {
        _logger.LogInfo($"   Procesando contenido básico...");

        // Preparar contenido con volanta
        var finalContent = PrepareContentWithVolanta(panopolyPage);

        // Preparar excerpt (bajada)
        var excerpt = !string.IsNullOrEmpty(panopolyPage.Bajada) ? panopolyPage.Bajada : "";

        // Obtener autor de WordPress (con fallback a admin)
        var authorId = _mappingService.GetWordPressUserId(panopolyPage.Uid);

        // Crear objeto Post de WordPress
        var wpPost = new Post
        {
            Title = new Title(panopolyPage.Title),
            Content = new Content(finalContent),
            Excerpt = new Excerpt(excerpt),
            Author = authorId,
            Status = panopolyPage.Status == 1 ? Status.Publish : Status.Draft,
            Date = panopolyPage.CreatedDate,
            // Note: WordPress API manejará la fecha de modificación automáticamente
        };

        _logger.LogInfo($"   Contenido preparado: {finalContent.Length} chars, autor: {authorId}");

        return wpPost;
    }

    /// <summary>
    /// Prepara el contenido final incluyendo bajada después del título y volanta al inicio
    /// </summary>
    private string PrepareContentWithVolanta(PanopolyPage panopolyPage)
    {
        var content = panopolyPage.Content ?? "";

        // Construir contenido final
        var finalContent = "";

        // 1. Agregar volanta al inicio si existe
        if (!string.IsNullOrEmpty(panopolyPage.Volanta))
        {
            finalContent += $@"<div class=""volanta"" style=""font-style: italic; color: #666; margin-bottom: 1em; font-size: 1.1em; border-left: 3px solid #0073aa; padding-left: 1em;"">
    {panopolyPage.Volanta}
</div>";
            //_logger.LogInfo($"   Volanta agregada: '{panopolyPage.Volanta.Substring(0, Math.Min(30, panopolyPage.Volanta.Length))}...'");
        }

        // 2. Agregar bajada después del título (si existe)
        if (!string.IsNullOrEmpty(panopolyPage.Bajada))
        {
            finalContent += $@"<div class=""bajada"" style=""font-size: 1.1em; font-weight: 500; color: #333; margin-bottom: 1.5em; line-height: 1.4;"">
    {panopolyPage.Bajada}
</div>";
            //_logger.LogInfo($"   Bajada agregada al contenido: '{panopolyPage.Bajada.Substring(0, Math.Min(30, panopolyPage.Bajada.Length))}...'");
        }

        // 3. Agregar contenido principal
        finalContent += content;

        if (string.IsNullOrEmpty(panopolyPage.Volanta) && string.IsNullOrEmpty(panopolyPage.Bajada))
        {
            _logger.LogInfo($"   Contenido original sin modificaciones");
        }

        return finalContent;
    }


    #endregion

    #region CATEGORÍAS Y TAGS

    /// <summary>
    /// Asigna categorías y tags al post de WordPress
    /// </summary>
    private void AssignCategoriesAndTags(Post wpPost, PanopolyPage panopolyPage)
    {
        _logger.LogInfo($"   Asignando categorías y tags...");

        var categories = new List<int>();
        var tags = new List<int>();

        // 1. CATEGORÍA PRINCIPAL (featured_categories)
        if (panopolyPage.CategoryId.HasValue)
        {
            var wpCategoryId = _mappingService.CategoryMapping.TryGetValue(panopolyPage.CategoryId.Value, out int catId) ? catId : (int?)null;
            if (wpCategoryId.HasValue)
            {
                categories.Add(wpCategoryId.Value);
                //_logger.LogInfo($"   Categoría principal: Drupal {panopolyPage.CategoryId} → WordPress {wpCategoryId}");
            }
            else
            {
                _logger.LogWarning($"   Categoría principal {panopolyPage.CategoryId} sin mapeo");
            }
        }

        // 2. REGIÓN COMO CATEGORÍA ADICIONAL
        if (panopolyPage.RegionId.HasValue)
        {
            var wpRegionId = _mappingService.GetWordPressRegion(panopolyPage.RegionId.Value);
            if (wpRegionId.HasValue)
            {
                categories.Add(wpRegionId.Value);
                //_logger.LogInfo($"   Región como categoría: Drupal {panopolyPage.RegionId} → WordPress {wpRegionId}");
            }
            else
            {
                _logger.LogWarning($"   Región {panopolyPage.RegionId} sin mapeo");
            }
        }

        // 3. TODOS LOS TAGS
        if (panopolyPage.Tags.Count > 0)
        {
            var wpTags = _mappingService.GetWordPressTags(panopolyPage.Tags);
            tags.AddRange(wpTags);

            var missingTags = panopolyPage.Tags.Count - wpTags.Count;
            //_logger.LogInfo($"   Tags: {wpTags.Count} asignados" + (missingTags > 0 ? $", {missingTags} sin mapeo" : ""));
        }
        
        // Asignar al post
        if (categories.Count > 0)
        {
            wpPost.Categories = categories.Distinct().ToList();
            //_logger.LogInfo($"   Total categorías asignadas: {wpPost.Categories.Count}");
        }

        if (tags.Count > 0)
        {
            wpPost.Tags = tags.Distinct().ToList();
            //_logger.LogInfo($"   Total tags asignados: {wpPost.Tags.Count}");
        }
    }

    #endregion

    #region GUARDADO DE MAPEO

    /// <summary>
    /// Guarda el mapeo entre Drupal y WordPress en la base de datos
    /// </summary>
    private async Task<MigratedPostInfo> SavePostMappingAsync(int drupalPostId, int wpPostId)
    {
        try
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            // Insertar mapeo
            await connection.ExecuteAsync(@"
                    INSERT INTO post_mapping_panopoly (drupal_post_id, wp_post_id, migrated_at) 
                    VALUES (@drupalId, @wpId, @migratedAt) 
                    ON DUPLICATE KEY UPDATE 
                        wp_post_id = @wpId, 
                        migrated_at = @migratedAt",
                new { drupalId = drupalPostId, wpId = wpPostId, migratedAt = DateTime.Now });

            _logger.LogInfo($"   Mapeo guardado: Drupal {drupalPostId} → WordPress {wpPostId}");

            return new MigratedPostInfo
            {
                DrupalPostId = drupalPostId,
                WpPostId = wpPostId,
                MigratedAt = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"   Error guardando mapeo: {ex.Message}");

            // Retornar info básica aunque falle el guardado
            return new MigratedPostInfo
            {
                DrupalPostId = drupalPostId,
                WpPostId = wpPostId,
                MigratedAt = DateTime.Now
            };
        }
    }
    #endregion

    #region MÉTODOS AUXILIARES

    /// <summary>
    /// Obtiene información resumida de la migración para logging
    /// </summary>
    public string GetMigrationSummary(PanopolyPage panopolyPage)
    {
        var hasVolanta = !string.IsNullOrEmpty(panopolyPage.Volanta);
        var hasBajada = !string.IsNullOrEmpty(panopolyPage.Bajada);
        var hasRegion = panopolyPage.RegionId.HasValue;
        var hasCategory = panopolyPage.CategoryId.HasValue;
        var tagCount = panopolyPage.Tags.Count;

        return $"Volanta: {(hasVolanta ? "✓" : "✗")}, " +
               $"Bajada: {(hasBajada ? "✓" : "✗")}, " +
               $"Región: {(hasRegion ? "✓" : "✗")}, " +
               $"Categoría: {(hasCategory ? "✓" : "✗")}, " +
               $"Tags: {tagCount}";
    }

    #endregion
}
