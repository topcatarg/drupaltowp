﻿using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Helpers;
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
using WordPressPCL.Client;
using WordPressPCL.Models;

namespace drupaltowp.Services;

internal class MappingService
{
    private readonly LoggerViewModel _logger;
    private readonly string _wpConnectionString;

    // 📋 MAPEOS CENTRALIZADOS
    public Dictionary<int, int> UserMapping { get; private set; } = new();
    public Dictionary<int, int> CategoryMapping { get; private set; } = new();
    public Dictionary<int, int> TagMapping { get; private set; } = new();
    public Dictionary<int, int> RegionMapping { get; private set; } = new();
    public Dictionary<int, int> MediaMapping { get; private set; } = new();

    // 📄 MAPEOS ESPECÍFICOS POR TIPO DE CONTENIDO
    public Dictionary<int, MigratedPostInfo> BibliotecaMapping { get; private set; } = new();
    public Dictionary<int, MigratedPostInfo> PanopolyMapping { get; private set; } = new();
    public Dictionary<int, MigratedPostInfo> PostMapping { get; private set; } = new();
    public Dictionary<int, MigratedPostInfo> OpinionMapping { get; private set; } = [];
    public Dictionary<int, MigratedPostInfo> HubsMapping { get; private set; } = [];
    public MappingService(LoggerViewModel logger, string wpConnectionString = null)
    {
        _logger = logger;
        _wpConnectionString = wpConnectionString ?? ConfiguracionGeneral.WPconnectionString;
    }

    #region MÉTODOS PRINCIPALES

    /// <summary>
    /// Carga todos los mapeos disponibles
    /// </summary>
    public async Task LoadAllMappingsAsync()
    {
        _logger.LogProcess("📋 Cargando todos los mapeos disponibles...");

        using var connection = new MySqlConnection(_wpConnectionString);
        await connection.OpenAsync();

        // Cargar mapeos básicos
        await LoadUserMappingAsync(connection);
        await LoadCategoryMappingAsync(connection);
        await LoadTagMappingAsync(connection);
        await LoadRegionMappingAsync(connection);
        await LoadMediaMappingAsync(connection);

        // Cargar mapeos de contenido
        await LoadBibliotecaMappingAsync(connection);
        await LoadPanopolyMappingAsync(connection);
        await LoadPostMappingAsync(connection);

        LogMappingSummary();
    }

    /// <summary>
    /// Carga solo los mapeos básicos (usuarios, categorías, tags, regiones)
    /// </summary>
    public async Task LoadBasicMappingsAsync()
    {
        _logger.LogProcess("📋 Cargando mapeos básicos...");

        using var connection = new MySqlConnection(_wpConnectionString);
        await connection.OpenAsync();

        await LoadUserMappingAsync(connection);
        await LoadCategoryMappingAsync(connection);
        await LoadTagMappingAsync(connection);
        await LoadRegionMappingAsync(connection);
        await LoadMediaMappingAsync(connection);
        _logger.LogSuccess($"✅ Mapeos básicos cargados");
    }

    /// <summary>
    /// Carga mapeos específicos para un tipo de contenido
    /// </summary>
    public async Task LoadMappingsForContentType(ContentType contentType)
    {
        _logger.LogProcess($"📋 Cargando mapeos para {contentType}...");

        using var connection = new MySqlConnection(_wpConnectionString);
        await connection.OpenAsync();

        // Siempre cargar básicos
        await LoadBasicMappingsAsync();

        // Cargar específico del tipo
        switch (contentType)
        {
            case ContentType.Biblioteca:
                await LoadBibliotecaMappingAsync(connection);
                break;
            case ContentType.PanopolyPage:
                await LoadPanopolyMappingAsync(connection);
                break;
            case ContentType.Post:
                await LoadPostMappingAsync(connection);
                break;
            case ContentType.Opinion:
                await LoadOpinionMappingAsync(connection);
                break;
            case ContentType.Hubs:
                await LoadHubsMappingAsync(connection);
                break;
        }

        LogMappingSummary();
    }

    #endregion

    #region MAPEOS BÁSICOS

    private async Task LoadUserMappingAsync(MySqlConnection connection)
    {
        try
        {
            var mappings = await connection.QueryAsync<dynamic>(
                "SELECT drupal_user_id, wp_user_id FROM user_mapping WHERE drupal_user_id IS NOT NULL");

            UserMapping = mappings.ToDictionary(x => (int)x.drupal_user_id, x => (int)x.wp_user_id);
            _logger.LogInfo($"   👥 Usuarios: {UserMapping.Count:N0}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error cargando usuarios: {ex.Message}");
            UserMapping = new Dictionary<int, int>();
        }
    }

    private async Task LoadCategoryMappingAsync(MySqlConnection connection)
    {
        try
        {
            // Cargar todas las categorías (tanto panopoly_categories como bibliteca_categorias)
            var mappings = await connection.QueryAsync<dynamic>(
                "SELECT drupal_category_id, wp_category_id FROM category_mapping WHERE drupal_category_id IS NOT NULL");

            CategoryMapping = mappings.ToDictionary(x => (int)x.drupal_category_id, x => (int)x.wp_category_id);
            _logger.LogInfo($"   📂 Categorías: {CategoryMapping.Count:N0}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error cargando categorías: {ex.Message}");
            CategoryMapping = new Dictionary<int, int>();
        }
    }

    private async Task LoadTagMappingAsync(MySqlConnection connection)
    {
        try
        {
            var mappings = await connection.QueryAsync<dynamic>(
                "SELECT drupal_tag_id, wp_tag_id FROM tag_mapping WHERE drupal_tag_id IS NOT NULL");

            TagMapping = mappings.ToDictionary(x => (int)x.drupal_tag_id, x => (int)x.wp_tag_id);
            _logger.LogInfo($"   🏷️ Tags: {TagMapping.Count:N0}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error cargando tags: {ex.Message}");
            TagMapping = new Dictionary<int, int>();
        }
    }

    private async Task LoadRegionMappingAsync(MySqlConnection connection)
    {
        try
        {
            var mappings = await connection.QueryAsync<dynamic>(@"
                    SELECT drupal_region, wp_category 
                    FROM panopoly_regiones_mapping 
                    WHERE drupal_region IS NOT NULL AND wp_category IS NOT NULL");

            RegionMapping = mappings.ToDictionary(x => (int)x.drupal_region, x => (int)x.wp_category);
            _logger.LogInfo($"   🌍 Regiones: {RegionMapping.Count:N0}");

            // Mostrar detalle de regiones si hay pocas
            if (RegionMapping.Count > 0 && RegionMapping.Count <= 10)
            {
                await ShowRegionMappingDetails(connection);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error cargando regiones: {ex.Message}");
            RegionMapping = new Dictionary<int, int>();
        }
    }

    private async Task LoadMediaMappingAsync(MySqlConnection connection)
    {
        try
        {
            var mappings = await connection.QueryAsync<dynamic>(
                "SELECT drupal_file_id, wp_media_id FROM media_mapping WHERE drupal_file_id IS NOT NULL");

            MediaMapping = mappings.ToDictionary(x => (int)x.drupal_file_id, x => (int)x.wp_media_id);
            _logger.LogInfo($"   🖼️ Media: {MediaMapping.Count:N0}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error cargando media: {ex.Message}");
            MediaMapping = new Dictionary<int, int>();
        }
    }

    #endregion

    #region MAPEOS DE CONTENIDO

    private async Task LoadBibliotecaMappingAsync(MySqlConnection connection)
    {
        try
        {
            var mappings = await connection.QueryAsync<MigratedPostInfo>(@"
                    SELECT 
                        drupal_post_id as DrupalPostId,
                        wp_post_id as WpPostId,
                        migrated_at as MigratedAt,
                        imagenes as Imagenes
                    FROM post_mapping_biblioteca");

            BibliotecaMapping = mappings.ToDictionary(
                x => x.DrupalPostId,
                x => new MigratedPostInfo
                {
                    DrupalPostId = x.DrupalPostId,
                    WpPostId = x.WpPostId,
                    MigratedAt = x.MigratedAt,
                    Imagenes = x.Imagenes
                });

            _logger.LogInfo($"   📚 Biblioteca: {BibliotecaMapping.Count:N0}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error cargando biblioteca: {ex.Message}");
            BibliotecaMapping = new Dictionary<int, MigratedPostInfo>();
        }
    }

    private async Task LoadPanopolyMappingAsync(MySqlConnection connection)
    {
        try
        {
            // Verificar si existe la tabla
            var tableExists = await CheckTableExistsAsync(connection, "post_mapping_panopoly");

            if (!tableExists)
            {
                _logger.LogInfo($"   📄 Panopoly: Tabla no existe - será creada");
                PanopolyMapping = new Dictionary<int, MigratedPostInfo>();
                return;
            }

            var mappings = await connection.QueryAsync<MigratedPostInfo>(@"
                    SELECT 
                        drupal_post_id as DrupalPostId,
                        wp_post_id as WpPostId,
                        migrated_at as MigratedAt
                    FROM post_mapping_panopoly");

            PanopolyMapping = mappings.ToDictionary(
                x => x.DrupalPostId,
                x => new MigratedPostInfo
                {
                    DrupalPostId = x.DrupalPostId,
                    WpPostId = x.WpPostId,
                    MigratedAt = x.MigratedAt
                });

            _logger.LogInfo($"   📄 Panopoly: {PanopolyMapping.Count:N0}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error cargando panopoly: {ex.Message}");
            PanopolyMapping = new Dictionary<int, MigratedPostInfo>();
        }
    }

    private async Task LoadPostMappingAsync(MySqlConnection connection)
    {
        try
        {
            var tableExists = await CheckTableExistsAsync(connection, "post_mapping");

            if (!tableExists)
            {
                _logger.LogInfo($"   📝 Posts: Tabla no existe");
                PostMapping = new Dictionary<int, MigratedPostInfo>();
                return;
            }

            var mappings = await connection.QueryAsync<MigratedPostInfo>(@"
                    SELECT 
                        drupal_post_id as DrupalPostId,
                        wp_post_id as WpPostId,
                        migrated_at as MigratedAt
                    FROM post_mapping");

            PostMapping = mappings.ToDictionary(
                x => x.DrupalPostId,
                x => new MigratedPostInfo
                {
                    DrupalPostId = x.DrupalPostId,
                    WpPostId = x.WpPostId,
                    MigratedAt = x.MigratedAt
                });

            _logger.LogInfo($"   📝 Posts: {PostMapping.Count:N0}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error cargando posts: {ex.Message}");
            PostMapping = new Dictionary<int, MigratedPostInfo>();
        }
    }

    private async Task LoadOpinionMappingAsync(MySqlConnection connection)
    {
        try
        {
            var mappings = await connection.QueryAsync<MigratedPostInfo>(@"
                    SELECT 
                        drupal_post_id as DrupalPostId,
                        wp_post_id as WpPostId,
                        migrated_at as MigratedAt
                    FROM post_mapping_opinion");

            OpinionMapping = mappings.ToDictionary(
                x => x.DrupalPostId,
                x => new MigratedPostInfo
                {
                    DrupalPostId = x.DrupalPostId,
                    WpPostId = x.WpPostId,
                    MigratedAt = x.MigratedAt,
                });

            _logger.LogInfo($"   📚 Opinion: {OpinionMapping.Count:N0}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error cargando opinion: {ex.Message}");
            OpinionMapping = [];
        }
    }

    private async Task LoadHubsMappingAsync(MySqlConnection connection)
    {
        try
        {
            var mappings = await connection.QueryAsync<MigratedPostInfo>(@"
                    SELECT 
                        drupal_post_id as DrupalPostId,
                        wp_post_id as WpPostId,
                        migrated_at as MigratedAt
                    FROM post_mapping_Hubs");

            
            HubsMapping = mappings.ToDictionary(
                x => x.DrupalPostId,
                x => new MigratedPostInfo
                {
                    DrupalPostId = x.DrupalPostId,
                    WpPostId = x.WpPostId,
                    MigratedAt = x.MigratedAt,
                });

            _logger.LogInfo($"   📚 Hubs: {HubsMapping.Count:N0}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error cargando hubs: {ex.Message}");
            HubsMapping = [];
        }
    }

    #endregion

    #region MÉTODOS AUXILIARES

    /// <summary>
    /// Obtiene el ID de usuario de WordPress, con fallback al admin (ID 1)
    /// </summary>
    public int GetWordPressUserId(int drupalUserId)
    {
        return UserMapping.TryGetValue(drupalUserId, out int wpUserId) ? wpUserId : 1;
    }

    public int GetWordPressMediaId(int drupalMediaId)
    {
        return MediaMapping.TryGetValue(drupalMediaId, out int wpMediaId) ? wpMediaId :1;
    }

    /// <summary>
    /// Obtiene las categorías de WordPress para una lista de IDs de Drupal
    /// </summary>
    public List<int> GetWordPressCategories(List<int> drupalCategoryIds)
    {
        return drupalCategoryIds
            .Where(id => CategoryMapping.ContainsKey(id))
            .Select(id => CategoryMapping[id])
            .ToList();
    }

    /// <summary>
    /// Obtiene los tags de WordPress para una lista de IDs de Drupal
    /// </summary>
    public List<int> GetWordPressTags(List<int> drupalTagIds)
    {
        return drupalTagIds
            .Where(id => TagMapping.ContainsKey(id))
            .Select(id => TagMapping[id])
            .ToList();
    }

    /// <summary>
    /// Obtiene la categoría de región de WordPress para un ID de región de Drupal
    /// </summary>
    public int? GetWordPressRegion(int? drupalRegionId)
    {
        if (!drupalRegionId.HasValue) return null;
        return RegionMapping.TryGetValue(drupalRegionId.Value, out int wpCategoryId) ? wpCategoryId : null;
    }

    /// <summary>
    /// Verifica si un post ya fue migrado
    /// </summary>
    public bool IsPostMigrated(int drupalPostId, ContentType contentType)
    {
        return contentType switch
        {
            ContentType.Biblioteca => BibliotecaMapping.ContainsKey(drupalPostId),
            ContentType.PanopolyPage => PanopolyMapping.ContainsKey(drupalPostId),
            ContentType.Post => PostMapping.ContainsKey(drupalPostId),
            ContentType.Opinion => OpinionMapping.ContainsKey(drupalPostId),
            ContentType.Hubs => HubsMapping.ContainsKey(drupalPostId),
            _ => false
        };
    }

    /// <summary>
    /// Obtiene el ID de WordPress de un post migrado
    /// </summary>
    public int? GetWordPressPostId(int drupalPostId, ContentType contentType)
    {
        return contentType switch
        {
            ContentType.Biblioteca => BibliotecaMapping.TryGetValue(drupalPostId, out var bibInfo) ? bibInfo.WpPostId : null,
            ContentType.PanopolyPage => PanopolyMapping.TryGetValue(drupalPostId, out var panInfo) ? panInfo.WpPostId : null,
            ContentType.Post => PostMapping.TryGetValue(drupalPostId, out var postInfo) ? postInfo.WpPostId : null,
            ContentType.Opinion => OpinionMapping.TryGetValue(drupalPostId, out var opiInfo) ? opiInfo.WpPostId: null,
            ContentType.Hubs => HubsMapping.TryGetValue(drupalPostId, out var hubsInfo) ? hubsInfo.WpPostId : null,
            _ => null
        };
    }

    private async Task<bool> CheckTableExistsAsync(MySqlConnection connection, string tableName)
    {
        var exists = await connection.QueryFirstOrDefaultAsync<int>(@"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_SCHEMA = DATABASE() 
                AND TABLE_NAME = @tableName",
            new { tableName });

        return exists > 0;
    }

    private async Task ShowRegionMappingDetails(MySqlConnection connection)
    {
        try
        {
            var wpCategoryIds = string.Join(",", RegionMapping.Values);
            var wpCategories = await connection.QueryAsync<dynamic>($@"
                    SELECT term_id, name 
                    FROM wp_terms 
                    WHERE term_id IN ({wpCategoryIds})");

            var wpCategoryNames = wpCategories.ToDictionary(x => (int)x.term_id, x => (string)x.name);

            _logger.LogInfo($"      📋 Detalle del mapeo de regiones:");
            foreach (var mapping in RegionMapping)
            {
                var wpCategoryName = wpCategoryNames.TryGetValue(mapping.Value, out var name) ? name : "Desconocida";
                _logger.LogInfo($"         Drupal {mapping.Key} → WP {mapping.Value} ({wpCategoryName})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error mostrando detalle de regiones: {ex.Message}");
        }
    }

    private void LogMappingSummary()
    {
        _logger.LogSuccess($"✅ Resumen de mapeos cargados:");
        _logger.LogInfo($"   👥 Usuarios: {UserMapping.Count:N0}");
        _logger.LogInfo($"   📂 Categorías: {CategoryMapping.Count:N0}");
        _logger.LogInfo($"   🏷️ Tags: {TagMapping.Count:N0}");
        _logger.LogInfo($"   🌍 Regiones: {RegionMapping.Count:N0}");
        _logger.LogInfo($"   🖼️ Media: {MediaMapping.Count:N0}");
        _logger.LogInfo($"   📚 Biblioteca migrada: {BibliotecaMapping.Count:N0}");
        _logger.LogInfo($"   📄 Panopoly migradas: {PanopolyMapping.Count:N0}");
        _logger.LogInfo($"   📝 Posts migrados: {PostMapping.Count:N0}");
    }

    #endregion

    #region GUARDAR MAPEOS

    public async Task SaveMediaMappingAsync(int drupalFid, int wpId, string filename)
    {
        using var connection = new MySqlConnection(_wpConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(@"
                INSERT INTO media_mapping (drupal_file_id, wp_media_id, drupal_filename, migrated_at) 
                VALUES (@drupalFid, @wpId, @filename, @migratedAt)
                ON DUPLICATE KEY UPDATE 
                    wp_media_id = @wpId, 
                    migrated_at = @migratedAt",
            new { drupalFid, wpId, filename, migratedAt = DateTime.Now });
        //Lo guardo en la lista tambien
        MediaMapping[drupalFid] = wpId;
    }

    public async Task SaveOpinionPostMappingAsync(int drupalId, int wpId)
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await connection.OpenAsync();

        MigratedPostInfo migratedPostInfo = new MigratedPostInfo()
        {
            DrupalPostId = drupalId,
            WpPostId = wpId,
            MigratedAt = DateTime.Now
        };
        await connection.ExecuteAsync(@"
                INSERT INTO post_mapping_opinion (drupal_post_id, wp_post_id,  migrated_at) 
                VALUES (@drupalId, @wpId, @migratedAt) 
                ON DUPLICATE KEY UPDATE 
                    wp_post_id = @wpId, 
                    migrated_at = @migratedAt",
            new
            {
                drupalId,
                wpId,
                migratedAt = DateTime.Now,
            });
        OpinionMapping[drupalId] = migratedPostInfo;
    }

    public async Task SaveHubsPostMappingAsync(int drupalId, int wpId)
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await connection.OpenAsync();

        MigratedPostInfo migratedPostInfo = new MigratedPostInfo()
        {
            DrupalPostId = drupalId,
            WpPostId = wpId,
            MigratedAt = DateTime.Now
        };
        await connection.ExecuteAsync(@"
                INSERT INTO post_mapping_hubs (drupal_post_id, wp_post_id,  migrated_at) 
                VALUES (@drupalId, @wpId, @migratedAt) 
                ON DUPLICATE KEY UPDATE 
                    wp_post_id = @wpId, 
                    migrated_at = @migratedAt",
            new
            {
                drupalId,
                wpId,
                migratedAt = DateTime.Now,
            });
        HubsMapping[drupalId] = migratedPostInfo;
    }

    private async Task SaveCategoryMappingAsync(int drupalId, int wordpressId, string drupalName, string vocabulary)
    {
        const string insertQuery = @"
            INSERT INTO category_mapping 
            (drupal_category_id, wp_category_id, drupal_name, vocabulary, migrated_at) 
            VALUES (@DrupalId, @WordPressId, @DrupalName, @Vocabulary, @MigratedAt)
            ON DUPLICATE KEY UPDATE 
                wp_category_id = VALUES(wp_category_id),
                drupal_name = VALUES(drupal_name),
                migrated_at = CURRENT_TIMESTAMP";

        using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await connection.ExecuteAsync(insertQuery, new
        {
            DrupalId = drupalId,
            WordPressId = wordpressId,
            DrupalName = drupalName,
            Vocabulary = vocabulary,
            MigratedAt = DateTime.Now
        });
        CategoryMapping[drupalId] = wordpressId;
    }
    #endregion


    #region CREACION DE CONTENIDO

    /// <summary>
    /// Migra una categoria que no existe a wordpress
    /// </summary>
    /// <param name="CategoryName">Nombre de la categoria</param>
    /// <param name="DrupalId">Id en drupal</param>
    /// <param name="_wpClient">Cliente wordpress activo</param>
    /// <returns>El id de la categoria creada</returns>
    public async Task<int> MigrateSingleCategory(string CategoryName, int DrupalId, WordPressClient _wpClient)
    {

        _logger.LogInfo($"Se va a crear la categoria {CategoryName}");
        // Crear nueva categoría
        var wpCategory = new Category
        {
            Name = CategoryName,
            Description = string.Empty,
            Slug = SlugHelpers.GenerateSlug(CategoryName),
        };

        var createdCategory = await _wpClient.Categories.CreateAsync(wpCategory);

        // Guardar mapeo en BD
        await SaveCategoryMappingAsync(DrupalId, createdCategory.Id, CategoryName, "");
        _logger.LogInfo($"Se agrego la {CategoryName} con el id {createdCategory.Id}");
        return createdCategory.Id;
    }

    /// <summary>
    /// Migra un archivo a wordpress
    /// </summary>
    /// <param name="drupalFile">Modelo de archivo de drupal</param>
    /// <param name="_wpClient">Cliente wordpress activo</param>
    /// <returns>el id del archivo migrado</returns>
    public async Task<int> MigrateSingleFileAsync(DrupalImage drupalFile, WordPressClient _wpClient)
    {
        try
        {
            var drupalPath = drupalFile.Uri.Replace("public://", "");
            var sourcePath = Path.Combine(ConfiguracionGeneral.DrupalFileRoute, drupalPath);

            if (!File.Exists(sourcePath))
            {
                _logger.LogWarning($"Archivo no encontrado: {sourcePath}");
                return 0;
            }

            // Usar la API de WordPress para subir el archivo
            using var fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            var mediaItem = await _wpClient.Media.CreateAsync(fileStream, drupalFile.Filename);

            //Lo agrego a los mapeos
            await SaveMediaMappingAsync(drupalFile.Fid, mediaItem.Id, drupalFile.Filename);
            return mediaItem.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error migrando archivo {drupalFile.Filename}: {ex.Message}");
            return 0;
        }
    }
    #endregion
}

/// <summary>
/// Enumeración para tipos de contenido
/// </summary>
public enum ContentType
{
    Post,
    Biblioteca,
    PanopolyPage,
    Opinion,
    Hubs
}

