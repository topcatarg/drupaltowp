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

namespace drupaltowp.Clases.Publicaciones.Panopoly;

internal class GetPanopolyPages
{
    private readonly LoggerViewModel _logger;
    private readonly MappingService _mappingService;

    public GetPanopolyPages(LoggerViewModel logger, MappingService mappingService)
    {
        _logger = logger;
        _mappingService = mappingService;
    }

    /// <summary>
    /// Obtiene todas las páginas panopoly desde Drupal con estadísticas completas
    /// </summary>
    public async Task<List<PanopolyPage>> GetPagesAsync()
    {
        _logger.LogProcess("Obteniendo páginas panopoly desde Drupal...");

        try
        {
            var pages = await ExecutePanopolyQueryAsync();
            var groupedPages = ProcessPagesData(pages);
            ShowStatistics(groupedPages);
            ValidateMappings(groupedPages);

            _logger.LogSuccess($"Páginas panopoly procesadas: {groupedPages.Count:N0}");
            return groupedPages;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error obteniendo páginas panopoly: {ex.Message}");
            throw;
        }
    }

    #region EJECUCIÓN DEL QUERY

    /// <summary>
    /// Ejecuta el query para obtener las páginas panopoly raw
    /// </summary>
    private async Task<List<PanopolyPage>> ExecutePanopolyQueryAsync()
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
        await connection.OpenAsync();

        var query = @"
                SELECT 
                    n.nid,
                    n.title,
                    n.created,
                    n.uid,
                    n.status,
                    fdb.body_value,
                    fdfb.field_bajada_value,
                    fdffc.field_featured_categories_tid,
                    fdfnrs.field_noticia_region_site_tid,
                    fdft.field_tags_tid,
                    fdfv.field_volanta_value
                FROM node n
                LEFT JOIN field_data_body fdb ON fdb.entity_id = n.nid
                LEFT JOIN field_data_field_bajada fdfb ON fdfb.entity_id = n.nid
                LEFT JOIN field_data_field_featured_categories fdffc ON fdffc.entity_id = n.nid
                LEFT JOIN field_data_field_noticia_region_site fdfnrs ON fdfnrs.entity_id = n.nid
                LEFT JOIN field_data_field_tags fdft ON fdft.entity_id = n.nid
                LEFT JOIN field_data_field_volanta fdfv ON fdfv.entity_id = n.nid
                WHERE n.type = 'panopoly_page'
                ORDER BY n.created DESC";

        _logger.LogInfo("Ejecutando query para páginas panopoly...");

        var rawResults = await connection.QueryAsync<PanopolyPage>(query);
        var rawList = rawResults.ToList();

        _logger.LogInfo($"Filas obtenidas del query: {rawList.Count:N0}");

        return rawList;
    }

    #endregion

    #region PROCESAMIENTO DE DATOS

    /// <summary>
    /// Procesa las filas raw agrupando por NID (maneja múltiples tags)
    /// </summary>
    private List<PanopolyPage> ProcessPagesData(List<PanopolyPage> rawPages)
    {
        _logger.LogProcess("Procesando datos y agrupando por NID...");

        var groupedPages = rawPages
            .GroupBy(p => p.Nid)
            .Select(group => GroupPanopolyPageRows(group))
            .ToList();

        _logger.LogInfo($"Páginas únicas después del agrupamiento: {groupedPages.Count:N0}");

        return groupedPages;
    }

    /// <summary>
    /// Agrupa las filas de una misma página panopoly (por los múltiples tags)
    /// </summary>
    private PanopolyPage GroupPanopolyPageRows(IGrouping<int, PanopolyPage> group)
    {
        var mainPage = group.First();

        // Recopilar todos los tags únicos de todas las filas
        var allTags = group
            .Where(p => p.Field_Tags_Tid.HasValue)
            .Select(p => p.Field_Tags_Tid.Value)
            .Distinct()
            .ToList();

        mainPage.Tags = allTags;

        // Para categoría y región, tomar el primero que no sea null
        if (!mainPage.Field_Featured_Categories_Tid.HasValue)
        {
            var categoryFromOtherRow = group.FirstOrDefault(p => p.Field_Featured_Categories_Tid.HasValue);
            if (categoryFromOtherRow != null)
                mainPage.Field_Featured_Categories_Tid = categoryFromOtherRow.Field_Featured_Categories_Tid;
        }

        if (!mainPage.Field_Noticia_Region_Site_Tid.HasValue)
        {
            var regionFromOtherRow = group.FirstOrDefault(p => p.Field_Noticia_Region_Site_Tid.HasValue);
            if (regionFromOtherRow != null)
                mainPage.Field_Noticia_Region_Site_Tid = regionFromOtherRow.Field_Noticia_Region_Site_Tid;
        }

        // Agregar la categoría a la lista si existe
        if (mainPage.Field_Featured_Categories_Tid.HasValue)
        {
            mainPage.Categories = new List<int> { mainPage.Field_Featured_Categories_Tid.Value };
        }

        return mainPage;
    }

    #endregion

    #region ESTADÍSTICAS Y VALIDACIÓN

    /// <summary>
    /// Muestra estadísticas detalladas de las páginas obtenidas
    /// </summary>
    private void ShowStatistics(List<PanopolyPage> pages)
    {
        _logger.LogInfo("ESTADÍSTICAS DE PÁGINAS PANOPOLY:");

        var stats = CalculateStatistics(pages);

        _logger.LogInfo($"   Total páginas: {stats.TotalPages:N0}");
        _logger.LogInfo($"   Publicadas: {stats.PublishedPages:N0}");
        _logger.LogInfo($"   Con contenido válido: {stats.WithValidContent:N0}");
        _logger.LogInfo($"   Con volanta: {stats.WithVolanta:N0}");
        _logger.LogInfo($"   Con bajada: {stats.WithBajada:N0}");
        _logger.LogInfo($"   Con región: {stats.WithRegion:N0}");
        _logger.LogInfo($"   Con categoría: {stats.WithCategory:N0}");
        _logger.LogInfo($"   Con tags: {stats.WithTags:N0}");
        _logger.LogInfo($"   Total tags: {stats.TotalTags:N0} (promedio: {stats.AvgTagsPerPage:F1} por página)");

        ShowExamplePages(pages);
    }

    /// <summary>
    /// Calcula estadísticas de las páginas
    /// </summary>
    private PageStatistics CalculateStatistics(List<PanopolyPage> pages)
    {
        return new PageStatistics
        {
            TotalPages = pages.Count,
            PublishedPages = pages.Count(p => p.Status == 1),
            WithValidContent = pages.Count(p => p.HasValidContent),
            WithVolanta = pages.Count(p => !string.IsNullOrEmpty(p.Volanta)),
            WithBajada = pages.Count(p => !string.IsNullOrEmpty(p.Bajada)),
            WithRegion = pages.Count(p => p.RegionId.HasValue),
            WithCategory = pages.Count(p => p.CategoryId.HasValue),
            WithTags = pages.Count(p => p.Tags.Count > 0),
            TotalTags = pages.Sum(p => p.Tags.Count),
            AvgTagsPerPage = pages.Count > 0 ? (double)pages.Sum(p => p.Tags.Count) / pages.Count : 0
        };
    }

    /// <summary>
    /// Muestra ejemplos de páginas para verificación
    /// </summary>
    private void ShowExamplePages(List<PanopolyPage> pages)
    {
        _logger.LogInfo("Ejemplos de páginas:");

        foreach (var page in pages.Take(3))
        {
            var volantaInfo = !string.IsNullOrEmpty(page.Volanta)
                ? $"Volanta: '{page.Volanta.Substring(0, Math.Min(30, page.Volanta.Length))}...'"
                : "Sin volanta";

            var regionInfo = page.RegionId.HasValue ? $"Región: {page.RegionId}" : "Sin región";
            var tagsInfo = page.Tags.Count > 0 ? $"Tags: [{string.Join(",", page.Tags)}]" : "Sin tags";

            _logger.LogInfo($"   [{page.Nid}] {page.Title}");
            _logger.LogInfo($"      {volantaInfo}, {regionInfo}, {tagsInfo}");
        }
    }

    /// <summary>
    /// Valida que existan los mapeos necesarios
    /// </summary>
    private void ValidateMappings(List<PanopolyPage> pages)
    {
        _logger.LogInfo("Validando mapeos disponibles...");

        var missingRegions = FindMissingRegions(pages);
        var missingCategories = FindMissingCategories(pages);

        if (missingRegions.Count > 0)
        {
            _logger.LogWarning($"ADVERTENCIA: {missingRegions.Count} regiones sin mapeo: [{string.Join(",", missingRegions)}]");
        }
        else
        {
            _logger.LogSuccess("Todas las regiones tienen mapeo disponible");
        }

        if (missingCategories.Count > 0)
        {
            _logger.LogWarning($"ADVERTENCIA: {missingCategories.Count} categorías sin mapeo: [{string.Join(",", missingCategories)}]");
        }
        else
        {
            _logger.LogSuccess("Todas las categorías tienen mapeo disponible");
        }
    }

    /// <summary>
    /// Encuentra regiones que no tienen mapeo a WordPress
    /// </summary>
    private List<int> FindMissingRegions(List<PanopolyPage> pages)
    {
        return pages
            .Where(p => p.RegionId.HasValue)
            .Select(p => p.RegionId.Value)
            .Distinct()
            .Where(regionId => !_mappingService.RegionMapping.ContainsKey(regionId))
            .ToList();
    }

    /// <summary>
    /// Encuentra categorías que no tienen mapeo a WordPress
    /// </summary>
    private List<int> FindMissingCategories(List<PanopolyPage> pages)
    {
        return pages
            .Where(p => p.CategoryId.HasValue)
            .Select(p => p.CategoryId.Value)
            .Distinct()
            .Where(catId => !_mappingService.CategoryMapping.ContainsKey(catId))
            .ToList();
    }

    #endregion

    #region CLASES AUXILIARES

    /// <summary>
    /// Estadísticas calculadas de las páginas
    /// </summary>
    private class PageStatistics
    {
        public int TotalPages { get; set; }
        public int PublishedPages { get; set; }
        public int WithValidContent { get; set; }
        public int WithVolanta { get; set; }
        public int WithBajada { get; set; }
        public int WithRegion { get; set; }
        public int WithCategory { get; set; }
        public int WithTags { get; set; }
        public int TotalTags { get; set; }
        public double AvgTagsPerPage { get; set; }
    }

    #endregion

}
