using System;
using System.Threading;
using System.Threading.Tasks;
using drupaltowp.Configuracion;
using drupaltowp.ViewModels;
using drupaltowp.Clases.Publicaciones.Panopoly;
using drupaltowp.Clases.Imagenes.Panopoly;
using drupaltowp.Clases.Imagenes;
using drupaltowp.Services;
using WordPressPCL;
using MySql.Data.MySqlClient;
using Dapper;

namespace drupaltowp.Business
{
    public class PanopolyMigrationBusiness
    {
        private readonly LoggerViewModel _logger;
        private readonly WordPressClient _wpClient;
        private readonly CancellationService _cancellationService;

        public PanopolyMigrationBusiness(LoggerViewModel logger, CancellationService cancellationService)
        {
            _logger = logger;
            _cancellationService = cancellationService;
            _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
            _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);
        }

        #region Análisis
        public async Task AnalyzePanopolyStructureAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Análisis Panopoly",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("🔍 Analizando estructura de páginas Panopoly...");

                    using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
                    await connection.OpenAsync();

                    // Verificar cancelación antes de cada consulta
                    cancellationToken.ThrowIfCancellationRequested();

                    // Analizar páginas panopoly
                    var pageCount = await connection.QueryFirstOrDefaultAsync<int>(
                        "SELECT COUNT(*) FROM node WHERE type = 'panopoly_page'");
                    _logger.LogInfo($"   📊 Total páginas panopoly: {pageCount:N0}");

                    cancellationToken.ThrowIfCancellationRequested();

                    // Analizar páginas con contenido
                    var pagesWithContent = await connection.QueryFirstOrDefaultAsync<int>(@"
                        SELECT COUNT(DISTINCT n.nid) 
                        FROM node n 
                        JOIN field_data_body b ON n.nid = b.entity_id 
                        WHERE n.type = 'panopoly_page' AND b.body_value IS NOT NULL");
                    _logger.LogInfo($"   📝 Con contenido: {pagesWithContent:N0}");

                    cancellationToken.ThrowIfCancellationRequested();

                    // Analizar páginas con volanta
                    var pagesWithVolanta = await connection.QueryFirstOrDefaultAsync<int>(@"
                        SELECT COUNT(DISTINCT n.nid) 
                        FROM node n 
                        JOIN field_data_field_volanta v ON n.nid = v.entity_id 
                        WHERE n.type = 'panopoly_page' AND v.field_volanta_value IS NOT NULL");
                    _logger.LogInfo($"   🏷️ Con volanta: {pagesWithVolanta:N0}");

                    cancellationToken.ThrowIfCancellationRequested();

                    // Analizar páginas con categorías
                    var pagesWithCategories = await connection.QueryFirstOrDefaultAsync<int>(@"
                        SELECT COUNT(DISTINCT n.nid) 
                        FROM node n 
                        JOIN field_data_field_featured_categories c ON n.nid = c.entity_id 
                        WHERE n.type = 'panopoly_page' AND c.field_featured_categories_tid IS NOT NULL");
                    _logger.LogInfo($"   📂 Con categorías: {pagesWithCategories:N0}");

                    cancellationToken.ThrowIfCancellationRequested();

                    // Analizar páginas con imágenes
                    var pagesWithImages = await connection.QueryFirstOrDefaultAsync<int>(@"
                        SELECT COUNT(DISTINCT fu.id) 
                        FROM file_usage fu
                        JOIN file_managed f ON fu.fid = f.fid
                        JOIN node n ON fu.id = n.nid
                        WHERE fu.type = 'node' 
                        AND n.type = 'panopoly_page'
                        AND f.filemime LIKE 'image/%'
                        AND f.status = 1");
                    _logger.LogInfo($"   🖼️ Con imágenes: {pagesWithImages:N0}");

                    _logger.LogSuccess("✅ Análisis de estructura completado");
                },
                timeoutMinutes: 5
            );
        }

        public async Task AnalyzeImageContentAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Análisis Imágenes Contenido",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("🔍 Iniciando análisis de imágenes en contenido...");


                    var imageMigrator = new PanopolyImageCheckerContent(_logger);

                    // Crear task de análisis
                    var analysisTask = imageMigrator.CheckImageOnContent(cancellationToken);

                    // Ejecutar con monitoreo de cancelación
                    var completedTask = await Task.WhenAny(
                        analysisTask,
                        Task.Delay(Timeout.Infinite, cancellationToken)
                    );

                    if (completedTask == analysisTask)
                    {
                        await analysisTask; // Asegurar que se complete correctamente
                        _logger.LogSuccess("✅ Análisis de imágenes en contenido completado");
                        _logger.LogInfo($"📁 Reporte guardado en: {ConfiguracionGeneral.LogFilePath}");
                    }
                    else
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                },
                timeoutMinutes: 30 // 30 minutos para análisis completo
            );
        }

        #endregion

        #region Migración de Páginas
        public async Task MigratePanopolyPagesAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Migración Páginas Panopoly",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("📄 Iniciando migración de páginas Panopoly...");

                    var migrator = new PanopolyMigrator(_logger);

                    // Configurar cancelación en el migrator
                    migrator.Cancelar = false;

                    // Monitorear cancelación en un task separado
                    var monitorTask = Task.Run(async () =>
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(500, cancellationToken);
                        }
                        migrator.Cancelar = true;
                    }, cancellationToken);

                    try
                    {
                        var migratedPosts = await migrator.MigratePanopolyPagesAsync();
                        _logger.LogSuccess($"✅ Páginas Panopoly migradas: {migratedPosts.Count:N0}");
                    }
                    finally
                    {
                        migrator.Cancelar = true; // Asegurar que se detenga
                    }
                },
                timeoutMinutes: 60 // 1 hora para migración completa
            );
        }
        #endregion

        #region Migración de Imágenes
        public async Task MigrateImagesAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Migración Imágenes",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("🖼️ Iniciando migración inteligente de imágenes...");

                    var imageMigrator = new PanopolyImageMigrator(_logger);

                    // El PanopolyImageMigrator necesitaría ser modificado para aceptar CancellationToken
                    // Por ahora usamos verificación periódica
                    var migratorTask = imageMigrator.SmartMigrateImagesAsync(cancellationToken);

                    // Crear task que se completa cuando se cancela o termina la migración
                    var completedTask = await Task.WhenAny(
                        migratorTask,
                        Task.Delay(Timeout.Infinite, cancellationToken)
                    );

                    if (completedTask == migratorTask)
                    {
                        var summary = await migratorTask;
                        _logger.LogSuccess($"✅ Migración de imágenes completada:");
                        _logger.LogInfo($"   📊 Posts procesados: {summary.TotalPostsProcessed:N0}");
                        _logger.LogInfo($"   🖼️ Con imagen genérica: {summary.PostsWithGenericImage:N0}");
                        _logger.LogInfo($"   📸 Con imagen original: {summary.PostsWithOriginalImage:N0}");
                        _logger.LogInfo($"   📁 Archivos procesados: {summary.FilesProcessed:N0}");
                        _logger.LogInfo($"   ✅ Archivos copiados: {summary.FilesCopied:N0}");
                        _logger.LogInfo($"   ♻️ Archivos existentes: {summary.FilesSkipped:N0}");
                    }
                    else
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                },
                timeoutMinutes: 120 // 2 horas para migración de imágenes
            );
        }

        public async Task ProcessContentImagesAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Procesamiento Imágenes Contenido",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("📝 Procesando imágenes en contenido...");

                    var smartImageMigrator = new SmartImageMigrator(_logger);

                    // Similar al anterior, necesitaría modificación para CancellationToken
                    var migratorTask = smartImageMigrator.SmartMigrateImagesAsync();

                    var completedTask = await Task.WhenAny(
                        migratorTask,
                        Task.Delay(Timeout.Infinite, cancellationToken)
                    );

                    if (completedTask == migratorTask)
                    {
                        var summary = await migratorTask;
                        _logger.LogSuccess($"✅ Procesamiento de imágenes de contenido completado:");
                        _logger.LogInfo($"   📊 Posts procesados: {summary.TotalPostsProcessed:N0}");
                        _logger.LogInfo($"   📁 Archivos procesados: {summary.FilesProcessed:N0}");
                        _logger.LogInfo($"   ✅ Archivos migrados: {summary.FilesCopied:N0}");
                        _logger.LogInfo($"   ♻️ Archivos existentes: {summary.FilesSkipped:N0}");
                    }
                    else
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                },
                timeoutMinutes: 90
            );
        }


        #endregion

        #region Migracion de contenido
        public async Task MigrateArchiveAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Migración Archivos",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("🖼️ Iniciando migración inteligente de archivos...");

                    var Migrator = new ArchiveMigrator(_logger);

                    // El PanopolyImageMigrator necesitaría ser modificado para aceptar CancellationToken
                    // Por ahora usamos verificación periódica
                    var migratorTask = Migrator.Migrator(cancellationToken);

                    // Crear task que se completa cuando se cancela o termina la migración
                    var completedTask = await Task.WhenAny(
                        migratorTask,
                        Task.Delay(Timeout.Infinite, cancellationToken)
                    );

                    if (completedTask == migratorTask)
                    {
                        /*
                        var summary = await migratorTask;
                        _logger.LogSuccess($"✅ Migración de imágenes completada:");
                        _logger.LogInfo($"   📊 Posts procesados: {summary.TotalPostsProcessed:N0}");
                        _logger.LogInfo($"   🖼️ Con imagen genérica: {summary.PostsWithGenericImage:N0}");
                        _logger.LogInfo($"   📸 Con imagen original: {summary.PostsWithOriginalImage:N0}");
                        _logger.LogInfo($"   📁 Archivos procesados: {summary.FilesProcessed:N0}");
                        _logger.LogInfo($"   ✅ Archivos copiados: {summary.FilesCopied:N0}");
                        _logger.LogInfo($"   ♻️ Archivos existentes: {summary.FilesSkipped:N0}");
                        */
                    }
                    else
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                },
                timeoutMinutes: 120 // 2 horas para migración de imágenes
            );
        }
        #endregion
        #region Estado y Validación (Sin cancelación - son operaciones rápidas)
        public async Task ShowPanopolyStatusAsync()
        {
            try
            {
                _logger.LogProcess("📊 Verificando estado de migración Panopoly...");

                using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
                await connection.OpenAsync();

                // Verificar tabla de mapeo panopoly
                var tableExists = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT COUNT(*) 
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_SCHEMA = DATABASE() 
                    AND TABLE_NAME = 'post_mapping_panopoly'");

                if (tableExists == 0)
                {
                    _logger.LogWarning("⚠️ Tabla post_mapping_panopoly no existe");
                    return;
                }

                // Contar páginas migradas
                var migratedPages = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM post_mapping_panopoly");
                _logger.LogInfo($"   📄 Páginas Panopoly migradas: {migratedPages:N0}");

                // Contar imágenes migradas
                var migratedImages = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM media_mapping");
                _logger.LogInfo($"   🖼️ Imágenes migradas: {migratedImages:N0}");

                // Verificar posts en WordPress
                var wpPosts = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM wp_posts WHERE post_type = 'post' AND post_status = 'publish'");
                _logger.LogInfo($"   📝 Posts publicados en WordPress: {wpPosts:N0}");

                // Últimas páginas migradas
                var recentPages = await connection.QueryAsync<dynamic>(@"
                    SELECT pm.drupal_post_id, pm.wp_post_id, wp.post_title, pm.migrated_at
                    FROM post_mapping_panopoly pm
                    JOIN wp_posts wp ON pm.wp_post_id = wp.ID
                    ORDER BY pm.migrated_at DESC
                    LIMIT 5");

                if (recentPages.Any())
                {
                    _logger.LogInfo("   📋 Últimas 5 páginas migradas:");
                    foreach (var page in recentPages)
                    {
                        _logger.LogInfo($"      [{page.drupal_post_id}→{page.wp_post_id}] {page.post_title}");
                    }
                }

                _logger.LogSuccess("✅ Estado de migración Panopoly verificado");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error verificando estado Panopoly: {ex.Message}");
                throw;
            }
        }

        public async Task ValidatePanopolyMigrationAsync()
        {
            try
            {
                _logger.LogProcess("🔍 Validando migración Panopoly...");

                using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
                await connection.OpenAsync();

                // Verificar integridad de posts
                var orphanedPosts = await connection.QueryAsync<dynamic>(@"
                    SELECT pm.drupal_post_id, pm.wp_post_id
                    FROM post_mapping_panopoly pm
                    LEFT JOIN wp_posts wp ON pm.wp_post_id = wp.ID
                    WHERE wp.ID IS NULL");

                if (orphanedPosts.Any())
                {
                    _logger.LogWarning($"⚠️ Encontrados {orphanedPosts.Count()} posts huérfanos en mapping");
                    foreach (var post in orphanedPosts.Take(5))
                    {
                        _logger.LogWarning($"   Huérfano: Drupal {post.drupal_post_id} → WP {post.wp_post_id}");
                    }
                }
                else
                {
                    _logger.LogSuccess("✅ Todos los posts en mapping existen en WordPress");
                }

                // Verificar posts sin categorías
                var postsWithoutCategories = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT COUNT(*)
                    FROM wp_posts wp
                    WHERE wp.post_type = 'post'
                    AND NOT EXISTS (
                        SELECT 1 FROM wp_term_relationships tr 
                        WHERE tr.object_id = wp.ID
                    )");

                if (postsWithoutCategories > 0)
                {
                    _logger.LogWarning($"⚠️ {postsWithoutCategories} posts sin categorías");
                }
                else
                {
                    _logger.LogSuccess("✅ Todos los posts tienen categorías asignadas");
                }

                // Verificar imágenes destacadas
                var postsWithFeaturedImage = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT COUNT(*)
                    FROM wp_posts wp
                    JOIN wp_postmeta pm ON wp.ID = pm.post_id
                    WHERE wp.post_type = 'post'
                    AND pm.meta_key = '_thumbnail_id'");

                var totalPosts = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM wp_posts WHERE post_type = 'post'");

                _logger.LogInfo($"📊 Posts con imagen destacada: {postsWithFeaturedImage}/{totalPosts}");

                _logger.LogSuccess("✅ Validación de migración Panopoly completada");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error validando migración Panopoly: {ex.Message}");
                throw;
            }
        }
        #endregion
    }
}