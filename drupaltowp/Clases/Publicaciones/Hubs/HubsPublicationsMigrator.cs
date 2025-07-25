using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Helpers;
using drupaltowp.Models;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using WordPressPCL;
using WordPressPCL.Client;
using WordPressPCL.Models;

namespace drupaltowp.Clases.Publicaciones.Hubs;


public class HubsPublicationsMigrator
{
    private readonly LoggerViewModel _logger;
    private readonly WordPressClient _wpClient;
    private readonly MappingService _mappingService;
    // Ya no necesitamos la categoría Hubs fija

    public bool Cancelar = false;

    public HubsPublicationsMigrator(LoggerViewModel logger)
    {
        _logger = logger;

        // Configurar cliente WordPress
        _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
        _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);

        // Inicializar servicio de mapeo
        _mappingService = new MappingService(_logger, ConfiguracionGeneral.WPconnectionString);
    }

    public async Task MigrateHubsPublicationsAsync()
    {
        try
        {
            _logger.LogProcess("🌐 Iniciando migración de publicaciones de hubs...");

            // 1. Cargar mapeos necesarios
            await LoadRequiredMappings();

            // 2. Obtener datos de hubs desde Drupal (ya no necesitamos crear categoría Hubs)
            var hubsData = await GetHubsDataFromDrupal();

            // 3. Agrupar datos por publicación
            var groupedHubs = GroupHubsByPublication(hubsData);

            _logger.LogInfo($"📊 Procesando {groupedHubs.Count} publicaciones de hubs...");

            // 4. Migrar cada publicación
            await MigrateHubsPublications(groupedHubs);

            _logger.LogSuccess("✅ Migración de publicaciones de hubs completada");
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Error en migración de hubs: {ex.Message}");
            throw;
        }
    }

    private async Task MigrateHubsPublications(Dictionary<int, HubPublicationData> groupedHubs)
    {
        var migratedCount = 0;
        var skippedCount = 0;
        var totalCount = groupedHubs.Count;

        foreach (var kvp in groupedHubs)
        {
            if (Cancelar) break;

            try
            {
                var hub = kvp.Value;

                // Verificar si ya está migrado
                if (_mappingService.HubsMapping.ContainsKey(hub.Nid))
                {
                    _logger.LogInfo($"⏭️ Hub {hub.Nid} ya migrado, omitiendo...");
                    skippedCount++;
                    continue;
                }

                // Crear el post en WordPress
                var wpPostId = await CreateWordPressPost(hub);

                // Guardar mapping
                await _mappingService.SaveHubsPostMappingAsync(hub.Nid, wpPostId);

                migratedCount++;
                var percentage = (double)(migratedCount + skippedCount) / totalCount * 100;
                _logger.LogInfo($"✅ Hub migrado: {hub.Titulo} (ID: {hub.Nid} → {wpPostId}) " +
                               $"- Progreso: {migratedCount + skippedCount:N0}/{totalCount:N0} ({percentage:F1}%)");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error migrando hub {kvp.Key}: {ex.Message}");
                skippedCount++;
                Cancelar = true;
            }
        }

        _logger.LogSuccess($"🎉 Migración completada: {migratedCount} hubs migrados, {skippedCount} omitidos");
    }

    private async Task<int> CreateWordPressPost(HubPublicationData hub)
    {
        // Crear la publicación directamente en la base de datos
        var postId = await CreatePostInDatabase(hub);

        // Agregar categorías y tags
        await AssignCategoriesAndTags(postId, hub);

        // Asignar imagen destacada
        await AssignFeaturedImage(postId, hub);

        return postId;
    }
    private async Task AssignFeaturedImage(int postId, HubPublicationData hub)
    {
        var featuredImageId = await HandleFeaturedImage(hub);

        if (featuredImageId > 0)
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(HubQueries.InsertFeaturedImageMeta,
                new { postId, imageId = featuredImageId });
        }
    }

    private async Task<int> HandleFeaturedImage(HubPublicationData hub)
    {
        if (hub.ImagenesDestacadas.Count < 1 || !hub.ImagenesDestacadas[0].HasValue)
            return 0;

        if (_mappingService.MediaMapping.TryGetValue(hub.ImagenesDestacadas[0].Value, out int wpImageId))
        {
            // Ya está mapeada, retornar ID de WordPress
            return wpImageId;
        }

        // Obtener la imagen de drupal
        var drupalImage = await ImageHelpers.GetDrupalImageData(hub.ImagenesDestacadas[0].Value);

        // Migrarla a WordPress
        int wpId = await _mappingService.MigrateSingleFileAsync(drupalImage, _wpClient);
        return wpId;
    }
    private async Task AssignCategoriesAndTags(int postId, HubPublicationData hub)
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await connection.OpenAsync();

        // Asignar categorías
        var categories = await GetWordPressCategories(hub);
        foreach (var categoryId in categories)
        {
            await connection.ExecuteAsync(HubQueries.InsertTermRelationship,
                new { postId, taxonomyId = categoryId });

            // Actualizar contador de la categoría
            await connection.ExecuteAsync(HubQueries.UpdateTaxonomyCount,
                new { taxonomyId = categoryId });
        }

        // Asignar tags
        var tags = await GetWordPressTags(hub);
        foreach (var tagId in tags)
        {
            await connection.ExecuteAsync(HubQueries.InsertTermRelationship,
                new { postId, taxonomyId = tagId });

            // Actualizar contador del tag
            await connection.ExecuteAsync(HubQueries.UpdateTaxonomyCount,
                new { taxonomyId = tagId });
        }
    }

    private async Task<List<int>> GetWordPressTags(HubPublicationData hub)
    {
        var wpTags = new List<int>();

        foreach (var tagId in hub.Tags)
        {
            using var wpConnection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await wpConnection.OpenAsync();

            if (_mappingService.TaxonomyMapping.TryGetValue(tagId, out int wpTagId))
            {
                wpTags.Add(wpTagId);
            }
            else
            {
                // Crear nuevo tag usando MappingService
                
                var tagName =await GetTaxonomyNameFromDrupalAsync(tagId);

                // Usar el método existente del MappingService
                int termTaxonomyId = await _mappingService.MigrateSingleTaxonomyDBDirectAsync(
                    tagName,
                    tagId,
                    "tag_hub",
                    "hub-tag-");

                wpTags.Add(termTaxonomyId);
            }
        }

        return wpTags;
    }

    private async Task<string> GetTaxonomyNameFromDrupalAsync(int id)
    {
        const string query = @"
            SELECT 
                t.name
            FROM taxonomy_term_data t
            WHERE tid = @id
            ";

        using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
        await connection.OpenAsync();
        string result = await connection.ExecuteScalarAsync<string>(query, new { id });
        return result;
    }
    private async Task<List<int>> GetWordPressCategories(HubPublicationData hub)
    {
        var categories = new List<int>();

        if (!hub.Categoria.HasValue)
        {
            return categories;
        }

        using var wpConnection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await wpConnection.OpenAsync();

        // Verificar si ya existe el mapeo
        if (_mappingService.TaxonomyMapping.TryGetValue(hub.Categoria.Value, out int wpCategoryId))
        {
                categories.Add(wpCategoryId);
        }
        else
        {
            // Usar el método existente del MappingService
            int termTaxonomyId = await _mappingService.MigrateSingleTaxonomyDBDirectAsync(
                hub.NombreCategoria,
                hub.Categoria.Value,
                "category_hub",
                "hub-category-");

            categories.Add(termTaxonomyId);
        }

        return categories;
    }
    private async Task<int> CreatePostInDatabase(HubPublicationData hub)
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await connection.OpenAsync();

        var dateGmt = hub.Creado.ToUniversalTime();
        var now = DateTime.Now;
        var nowGmt = now.ToUniversalTime();
        var slug = GenerateSlug(hub.Titulo);

        var postId = await connection.QuerySingleAsync<int>($@"
            {HubQueries.InsertWordPressPost};
            {HubQueries.GetLastInsertId}",
            new
            {
                author = _mappingService.UserMapping[hub.Uid],
                date = hub.Creado.ToString("yyyy-MM-dd HH:mm:ss"),
                date_gmt = dateGmt.ToString("yyyy-MM-dd HH:mm:ss"),
                content = hub.Cuerpo ?? "",
                title = hub.Titulo,
                excerpt = hub.Bajada ?? "", // Bajada como excerpt
                slug = slug,
                modified = now.ToString("yyyy-MM-dd HH:mm:ss"),
                modified_gmt = nowGmt.ToString("yyyy-MM-dd HH:mm:ss"),
                guid = $"{ConfiguracionGeneral.UrlsitioWP}/?post_type=hub&p={{0}}"
            });

        // Actualizar el GUID con el ID real
        await connection.ExecuteAsync(HubQueries.UpdatePostGuid,
            new
            {
                guid = $"{ConfiguracionGeneral.UrlsitioWP}/?post_type=hub&p={postId}",
                postId
            });

        // Agregar volanta como meta field si existe
        if (!string.IsNullOrWhiteSpace(hub.Volanta))
        {
            await connection.ExecuteAsync(HubQueries.InsertPostMeta,
                new { postId, metaKey = "_hub_volanta", metaValue = hub.Volanta });
        }

        return postId;
    }

    private string GenerateSlug(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "hub-sin-titulo";

        // Convertir a minúsculas y remover acentos
        var slug = title.ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u")
            .Replace("ñ", "n").Replace("ü", "u");

        // Remover caracteres especiales y espacios
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');

        // Limitar longitud
        if (slug.Length > 100)
            slug = slug.Substring(0, 100).TrimEnd('-');

        return string.IsNullOrWhiteSpace(slug) ? "hub-sin-titulo" : slug;
    }

    private Dictionary<int, HubPublicationData> GroupHubsByPublication(List<HubQueryResult> hubsData)
    {
        _logger.LogInfo("📋 Agrupando datos por publicación...");

        var groupedHubs = new Dictionary<int, HubPublicationData>();

        foreach (var row in hubsData)
        {
            int nid = row.Nid;

            if (!groupedHubs.ContainsKey(nid))
            {
                // Crear nueva entrada
                groupedHubs[nid] = new HubPublicationData
                {
                    Nid = nid,
                    Titulo = row.Titulo,
                    Uid = row.Uid,
                    Creado = row.Creado,
                    Status = row.Status,
                    Cuerpo = row.Cuerpo,
                    Bajada = row.Bajada,
                    Volanta = row.Volanta, // Agregar volanta
                    Categoria = row.Categoria,
                    NombreCategoria = row.Nombre_Categoria,
                    ImagenesDestacadas = new List<int?>(),
                    Tags = new List<int>()
                };
            }

            var hub = groupedHubs[nid];

            // Agregar imagen destacada si existe y no está ya agregada
            if (row.Imagen_Destacada.HasValue && !hub.ImagenesDestacadas.Contains(row.Imagen_Destacada))
            {
                hub.ImagenesDestacadas.Add(row.Imagen_Destacada);
            }

            // Agregar tag si existe y no está ya agregado
            if (row.Tags.HasValue && !hub.Tags.Contains(row.Tags.Value))
            {
                hub.Tags.Add(row.Tags.Value);
            }
        }

        _logger.LogInfo($"📊 Agrupados en {groupedHubs.Count} publicaciones únicas");
        return groupedHubs;
    }
    private async Task<List<HubQueryResult>> GetHubsDataFromDrupal()
    {
        _logger.LogInfo("📥 Obteniendo datos de hubs desde Drupal...");

        using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
        await connection.OpenAsync();

        var hubsData = await connection.QueryAsync<HubQueryResult>(HubQueries.GetHubsFromDrupal);
        _logger.LogInfo($"📊 Obtenidos {hubsData.Count()} registros de hubs");

        return hubsData.ToList();
    }

    private async Task LoadRequiredMappings()
    {
        _logger.LogInfo("📋 Cargando mapeos necesarios...");
        await _mappingService.LoadMappingsForContentType(ContentType.Hubs);
    }
}