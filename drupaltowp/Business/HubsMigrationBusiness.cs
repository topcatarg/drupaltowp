using Dapper;
using drupaltowp.Clases.Publicaciones.Hubs;
using drupaltowp.Clases.Publicaciones.Opinion;
using drupaltowp.Configuracion;
using drupaltowp.Models;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Models;

namespace drupaltowp.Business;

public class HubsMigrationBusiness
{
    private readonly LoggerViewModel _logger;
    private readonly CancellationService _cancellationService;
    private readonly string _drupalConnectionString;
    private readonly string _wpConnectionString;
    private readonly WordPressClient _wpClient;
    private readonly MappingService _mappingService;

    public HubsMigrationBusiness(LoggerViewModel logger, CancellationService cancellationService)
    {
        _logger = logger;
        _cancellationService = cancellationService;
        _drupalConnectionString = ConfiguracionGeneral.DrupalconnectionString;
        _wpConnectionString = ConfiguracionGeneral.WPconnectionString;

        // Configurar cliente WordPress
        _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
        _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);

        // Inicializar servicio de mapeo
        _mappingService = new MappingService(_logger, _wpConnectionString);
    }

    /// <summary>
    /// Analiza la estructura de hubs en Drupal
    /// </summary>
    public async Task AnalyzeHubsStructureAsync()
    {
        try
        {
            _logger.LogProcess("🔍 Iniciando análisis de estructura de hubs...");
            
            var analyzer = new HubsAnalyzer(_logger, _cancellationService);
            await analyzer.AnalyzeHubsStructureAsync();

            _logger.LogSuccess("✅ Análisis de hubs completado");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error en análisis de hubs: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Migra las publicaciones de hubs de Drupal a WordPress
    /// </summary>
    public async Task MigrateHubsPublicationsAsync()
    {
        await _cancellationService.ExecuteOperationAsync(
                "Migración Páginas Hubs",
                async (cancellationToken) =>
                {
                    var migrator = new HubsPublicationsMigrator(_logger)
                    {
                        // Configurar cancelación en el migrator
                        Cancelar = false
                    };

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
                        await migrator.MigrateHubsPublicationsAsync();
                    }
                    finally
                    {
                        migrator.Cancelar = true; // Asegurar que se detenga
                    }
                },
                timeoutMinutes: 60 // 1 hora para migración completa
            );
    }

    /// <summary>
    /// Migra las imágenes asociadas a los hubs
    /// </summary>
    public async Task MigrateHubsImagesAsync()
    {
        try
        {
            _logger.LogProcess("🖼️ Iniciando migración de imágenes de hubs...");

            var imageMigrator = new HubsImagesMigrator(_logger, _cancellationService);
            await imageMigrator.MigrateHubsImagesAsync();

            _logger.LogSuccess("✅ Migración de imágenes de hubs completada");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error en migración de imágenes de hubs: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Procesa las imágenes dentro del contenido de los hubs
    /// </summary>
    public async Task ProcessHubsContentImagesAsync()
    {
        try
        {
            _logger.LogProcess("🔗 Iniciando procesamiento de imágenes en contenido de hubs...");

            var contentProcessor = new HubsContentImageProcessor(_logger, _cancellationService);
            await contentProcessor.ProcessHubsContentImagesAsync();

            _logger.LogSuccess("✅ Procesamiento de imágenes en contenido completado");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error procesando imágenes en contenido de hubs: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Muestra el estado actual de la migración de hubs
    /// </summary>
    public async Task ShowHubsStatusAsync()
    {
        try
        {
            _logger.LogProcess("📊 Obteniendo estado de migración de hubs...");

            // Cargar mapping actual
            await _mappingService.LoadMappingsForContentType(ContentType.Hubs);

            using var drupalConnection = new MySqlConnection(_drupalConnectionString);
            await drupalConnection.OpenAsync();

            // Contar hubs en Drupal
            var drupalHubsCount = await drupalConnection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM node WHERE type = 'hub' AND status = 1");

            using var wpConnection = new MySqlConnection(_wpConnectionString);
            await wpConnection.OpenAsync();

            // Contar posts migrados
            var migratedCount = _mappingService.HubsMapping.Count;

            // Estadísticas detalladas
            _logger.LogInfo($"📊 ESTADO MIGRACIÓN HUBS:");
            _logger.LogInfo($"   🗂️ Hubs en Drupal: {drupalHubsCount}");
            _logger.LogInfo($"   ✅ Hubs migrados: {migratedCount}");
            _logger.LogInfo($"   📈 Progreso: {(migratedCount * 100.0 / drupalHubsCount):F1}%");

            if (migratedCount > 0)
            {
                var withImages = _mappingService.HubsMapping.Values.Count(h => h.Imagenes);
                _logger.LogInfo($"   🖼️ Hubs con imágenes: {withImages}");

                var lastMigrated = _mappingService.HubsMapping.Values
                    .OrderByDescending(h => h.MigratedAt)
                    .FirstOrDefault();

                if (lastMigrated != null)
                {
                    _logger.LogInfo($"   🕒 Última migración: {lastMigrated.MigratedAt:yyyy-MM-dd HH:mm}");
                }
            }

            _logger.LogSuccess("✅ Estado obtenido correctamente");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error obteniendo estado: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Valida la migración de hubs comparando datos entre Drupal y WordPress
    /// </summary>
    public async Task ValidateHubsMigrationAsync()
    {
        try
        {
            _logger.LogProcess("✅ Validando migración de hubs...");

            await _mappingService.LoadMappingsForContentType(ContentType.Hubs);

            int validatedCount = 0;
            int errorsCount = 0;

            foreach (var mapping in _mappingService.HubsMapping.Take(10)) // Validar una muestra
            {
                try
                {
                    // Obtener post de WordPress
                    var wpPost = await _wpClient.Posts.GetByIDAsync(mapping.Value.WpPostId);

                    if (wpPost != null)
                    {
                        validatedCount++;
                        _logger.LogInfo($"✅ Hub válido: Drupal {mapping.Key} → WP {mapping.Value.WpPostId}");
                    }
                    else
                    {
                        errorsCount++;
                        _logger.LogWarning($"⚠️ Hub no encontrado en WP: {mapping.Value.WpPostId}");
                    }

                    await Task.Delay(100); // No sobrecargar la API
                }
                catch (Exception ex)
                {
                    errorsCount++;
                    _logger.LogError($"❌ Error validando hub {mapping.Key}: {ex.Message}");
                }
            }

            _logger.LogSuccess($"🎉 Validación completada: {validatedCount} válidos, {errorsCount} errores");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error en validación: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Borra todas las publicaciones generadas para volver a empezar
    /// </summary>
    /// <returns></returns>
    public async Task RollBackMigrationAsync()
    {
        await _cancellationService.ExecuteOperationAsync(
                "Rollback Migración Páginas Hubs",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("📄 Iniciando rollback migración de páginas Hubs...");

                    var migrator = new HubsPublicationsRollback(_logger)
                    {
                        // Configurar cancelación en el migrator
                        Cancelar = false
                    };

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
                        await migrator.RollbackPublicationsAsync();
                    }
                    finally
                    {
                        migrator.Cancelar = true; // Asegurar que se detenga
                    }
                },
                timeoutMinutes: 60 // 1 hora para migración completa
            );
    }
}