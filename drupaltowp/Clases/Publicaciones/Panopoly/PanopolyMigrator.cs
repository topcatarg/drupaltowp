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

namespace drupaltowp.Clases.Publicaciones.Panopoly;

internal class PanopolyMigrator
{

    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly MappingService _mappingService;

    public bool Cancelar { get; set; } = false;

    public PanopolyMigrator(LoggerViewModel logger)
    {
        _logger = logger;
        _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
        _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);
        _mappingService = new MappingService(logger);
    }

    public async Task<Dictionary<int, MigratedPostInfo>> MigratePanopolyPagesAsync()
    {
        _logger.LogProcess("INICIANDO MIGRACIÓN DE PANOPOLY PAGES");

        // 🧹 VARIABLE PARA TESTING - Limpiar migración anterior
        const bool LIMPIAR_MIGRACION_ANTERIOR = true; // Cambiar a false en producción

        try
        {
            // ✅ PASO 0: LIMPIAR MIGRACIÓN ANTERIOR SI ESTÁ HABILITADO
            if (LIMPIAR_MIGRACION_ANTERIOR)
            {
                await CleanupPreviousMigrationAsync();
            }

            // ✅ PASO 1: CARGAR MAPEOS USANDO EL SERVICIO
            await _mappingService.LoadMappingsForContentType(ContentType.PanopolyPage);

            // ✅ PASO 2: OBTENER PÁGINAS PANOPOLY DE DRUPAL (DELEGADO)
            var getPagesService = new GetPanopolyPages(_logger, _mappingService);
            var panopolyPages = await getPagesService.GetPagesAsync();

            if (panopolyPages.Count == 0)
            {
                _logger.LogWarning("No se encontraron páginas panopoly para migrar");
                return _mappingService.PanopolyMapping;
            }

            // ✅ PASO 3: MIGRAR CADA PÁGINA A WORDPRESS
            var migratedPosts = await MigrateAllPagesAsync(panopolyPages);

            _logger.LogCompleted($"Migración completada: {migratedPosts.Count} páginas procesadas");
            return migratedPosts;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error en migración de panopoly: {ex.Message}");
            throw;
        }
    }

    #region MIGRACIÓN MASIVA

    /// <summary>
    /// Migra todas las páginas panopoly usando el servicio especializado
    /// </summary>
    private async Task<Dictionary<int, MigratedPostInfo>> MigrateAllPagesAsync(List<PanopolyPage> panopolyPages)
    {
        _logger.LogProcess($"Migrando {panopolyPages.Count:N0} páginas panopoly...");

        var migrationService = new MigratePanopolyPost(_logger, _wpClient, _mappingService);
        var results = new Dictionary<int, MigratedPostInfo>();

        int processed = 0;
        int migrated = 0;
        int skipped = 0;
        int errors = 0;
        int total = panopolyPages.Count;

        foreach (var page in panopolyPages)
        {
            // Verificar cancelación
            if (Cancelar)
            {
                _logger.LogWarning("Migración cancelada por el usuario");
                break;
            }

            processed++;

            try
            {
                // Validar contenido básico directamente
                if (string.IsNullOrEmpty(page.Title) || string.IsNullOrEmpty(page.Content))
                {
                    skipped++;
                    _logger.LogWarning($"   [{processed}/{total}] Omitida: [{page.Nid}] {page.Title} - sin título o contenido");
                    continue;
                }

                // Migrar la página
                var result = await migrationService.MigratePostAsync(page);
                results[page.Nid] = result;

                migrated++;

                // Mostrar resumen de la migración con progreso
                var summary = migrationService.GetMigrationSummary(page);
                _logger.LogSuccess($"   [{processed}/{total}] [{page.Nid}] {page.Title} - {summary}");

                // Log progreso cada 5 páginas
                if (processed % 50 == 0)
                {
                    var percentage = (processed * 100.0) / total;
                    _logger.LogInfo($"📊 Progreso: {processed:N0}/{total:N0} ({percentage:F1}%) - Migradas: {migrated}, Omitidas: {skipped}, Errores: {errors}");
                }

                // Pequeña pausa para no sobrecargar
                //await Task.Delay(50);
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError($"   [{processed}/{total}] Error [{page.Nid}] {page.Title}: {ex.Message}");
            }
        }

        // Resumen final
        _logger.LogInfo($"📊 RESUMEN DE MIGRACIÓN:");
        _logger.LogInfo($"   📄 Total procesadas: {processed:N0}");
        _logger.LogInfo($"   ✅ Migradas exitosamente: {migrated:N0}");
        _logger.LogInfo($"   ⏭️ Omitidas: {skipped:N0}");
        _logger.LogInfo($"   ❌ Errores: {errors:N0}");

        return results;
    }

    /// <summary>
    /// Limpia la migración anterior para testing
    /// </summary>
    private async Task CleanupPreviousMigrationAsync()
    {
        _logger.LogWarning("🧹 LIMPIANDO MIGRACIÓN ANTERIOR (MODO TESTING)...");

        try
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            // Obtener IDs de WordPress de posts migrados
            var wpPostIds = await connection.QueryAsync<int>(@"
                    SELECT wp_post_id 
                    FROM post_mapping_panopoly");

            var postIds = wpPostIds.ToList();

            if (postIds.Count > 0)
            {
                _logger.LogWarning($"   Eliminando {postIds.Count} posts de WordPress...");

                // Eliminar posts de WordPress usando la API
                foreach (var postId in postIds)
                {
                    try
                    {
                        await _wpClient.Posts.DeleteAsync(postId, true); // true = force delete
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"   Error eliminando post {postId}: {ex.Message}");
                    }
                }

                // Limpiar tabla de mapeo
                await connection.ExecuteAsync("DELETE FROM post_mapping_panopoly");

                _logger.LogSuccess($"   ✅ Limpieza completada: {postIds.Count} posts eliminados");
            }
            else
            {
                _logger.LogInfo("   No hay posts anteriores para limpiar");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"   Error en limpieza: {ex.Message}");
        }
    }
    #endregion

}
