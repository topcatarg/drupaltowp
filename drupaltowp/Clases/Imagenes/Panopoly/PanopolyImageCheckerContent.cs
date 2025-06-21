using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace drupaltowp.Clases.Imagenes.Panopoly;

class PanopolyImageCheckerContent(LoggerViewModel _logger)
{

    /// <summary>
    /// Analiza cómo están estructuradas las imágenes en el contenido de las publicaciones
    /// para optimizar el proceso de migración
    /// </summary>
    public async Task CheckImageOnContent(CancellationToken cancellationToken = default)
    {
        var fileLogger = new FileLogger();

        try
        {
            _logger.LogProcess("🔍 Analizando FIDs y nombres de archivo en contenido...");

            await fileLogger.LogAsync("=".PadRight(80, '='));
            await fileLogger.LogAsync($"ANÁLISIS SIMPLE DE IMÁGENES - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            await fileLogger.LogAsync("=".PadRight(80, '='));

            // Obtener posts con contenido
            var posts = await GetPostsWithContentAsync(cancellationToken);
            _logger.LogInfo($"📊 Posts a analizar: {posts.Count:N0}");

            await fileLogger.LogAsync($"Posts analizados: {posts.Count:N0}");

            // Contadores
            int postsWithFids = 0;
            int postsWithFileNames = 0;
            int totalFids = 0;
            int totalFileNames = 0;
            var uniqueFids = new HashSet<int>();
            var uniqueFileNames = new HashSet<string>();

            int processed = 0;
            foreach (var post in posts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var archivos = await GetFilesFromId(post.DrupalPostId);
                    var analysis = AnalyzePostContent(post.PostContent, archivos);

                    if (analysis.Cadenas.Count > 0)
                    {
                        await fileLogger.LogAsync($"\n[{post.WpPostId}] {post.PostTitle} - {post.PostDate:yyyy-MM-dd}");
                        await fileLogger.LogAsync($"   Drupal: {post.DrupalPostId}");
                        foreach (var cadena in analysis.Cadenas)
                        {
                            await fileLogger.LogAsync($"   - {cadena}");
                        }
                    }

                    processed++;
                    cancellationToken.ThrowIfCancelledEvery(processed, 50);

                    if (processed % 100 == 0)
                    {
                        _logger.LogProgress("Analizando", processed, posts.Count, 100);
                    }
                }
                catch (OperationCanceledException)
                {
                    await fileLogger.LogAsync($"\n❌ CANCELADO en post {post.WpPostId} después de {processed} posts");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error en post {post.WpPostId}: {ex.Message}");
                }
            }

            // Resumen final
            await LogSummaryAsync(fileLogger, processed, postsWithFids, postsWithFileNames,
                                 totalFids, totalFileNames, uniqueFids, uniqueFileNames);

            _logger.LogSuccess($"✅ Análisis completado: {processed} posts analizados");
            _logger.LogInfo($"📁 FIDs: {uniqueFids.Count} únicos, Archivos: {uniqueFileNames.Count} únicos");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("⏹️ Análisis cancelado por el usuario");
            await fileLogger.LogAsync($"\n⏹️ ANÁLISIS CANCELADO - {DateTime.Now}");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"💥 Error en análisis: {ex.Message}");
            await fileLogger.LogErrorAsync("Error general", ex);
            throw;
        }
    }
    /// <summary>
    /// Obtiene posts con contenido para analizar
    /// </summary>
    private async Task<List<SimplePostData>> GetPostsWithContentAsync(CancellationToken cancellationToken)
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
        await connection.OpenAsync();

        cancellationToken.ThrowIfCancellationRequested();

        // Query optimizado para obtener posts con contenido no vacío
        var query = @"
                SELECT 
            wp.ID as WpPostId,
            wp.post_title as PostTitle,
            wp.post_content as PostContent,
            wp.post_date as PostDate,
            pmp.drupal_post_id as DrupalPostId
        FROM post_mapping_panopoly pmp 
        LEFT JOIN wp_posts wp  ON wp.ID = pmp.wp_post_id
        ORDER BY wp.post_date DESC";

        var posts = await connection.QueryAsync<SimplePostData>(query);
        return posts.ToList();
    }

    /// <summary>
    /// Analiza contenido de un post (solo FIDs y nombres de archivo)
    /// </summary>
    private static SimpleAnalysis AnalyzePostContent(string content, List< DatosSimples> archivos)
    {
        var analysis = new SimpleAnalysis();
        foreach (var archivo in archivos)
        {
            string fid = "$\"\\\"fid\\\":\\\"{archivo.Fid}\\\"\"";
            if (content.Contains(fid, StringComparison.OrdinalIgnoreCase))
            {
                analysis.Cadenas.Add("Contiene FID");

            }
            int posicion = content.IndexOf(archivo.FileName);
            while (posicion > 0)
            {
                int posicioninicial = -1;
                int posicionfinal = -1;
                for (int i = posicion; i >= 0; i--)
                {
                    if (content[i] == '<')
                    {
                        posicioninicial = i;
                        break;
                    }
                }
                if (posicioninicial >= 0)
                {
                    for (int i = posicion + archivo.FileName.Length; i < content.Length; i++)
                    {
                        if (content[i] == '>')
                        {
                            posicionfinal = i;
                            break;
                        }
                    }
                }
                if (posicionfinal >= 0 && posicioninicial >= 0)
                {
                    analysis.Cadenas.Add($"Contiene nombre de archivo entre < y >: {content.Substring(posicioninicial, posicionfinal - posicioninicial + 1)}");
                }
                posicion = content.IndexOf(archivo.FileName,posicion+1);
            }
        }
        return analysis;
    }

    /// <summary>
    /// Log de un post individual
    /// </summary>
    /*private async Task LogPostAsync(SimplePostData post, SimpleAnalysis analysis, FileLogger fileLogger)
    {
        await fileLogger.LogAsync($"\n[{post.WpPostId}] {post.PostTitle}");
        await fileLogger.LogAsync($"   Drupal: {post.DrupalPostId} | Fecha: {post.PostDate:yyyy-MM-dd}");

        if (analysis.Fids.Any())
        {
            await fileLogger.LogAsync($"   🎯 FIDs ({analysis.Fids.Count}): {string.Join(", ", analysis.Fids.OrderBy(f => f))}");
        }

        if (analysis.FileNames.Any())
        {
            await fileLogger.LogAsync($"   📁 Archivos ({analysis.FileNames.Count}): {string.Join(", ", analysis.FileNames.Take(5))}");
            if (analysis.FileNames.Count > 5)
                await fileLogger.LogAsync($"       ... y {analysis.FileNames.Count - 5} más");
        }
    }*/

    /// <summary>
    /// Log del resumen final
    /// </summary>
    private async Task LogSummaryAsync(FileLogger fileLogger, int totalPosts, int postsWithFids,
        int postsWithFileNames, int totalFids, int totalFileNames,
        HashSet<int> uniqueFids, HashSet<string> uniqueFileNames)
    {
        await fileLogger.LogAsync($"\n" + "=".PadRight(80, '='));
        await fileLogger.LogAsync("RESUMEN FINAL");
        await fileLogger.LogAsync("=".PadRight(80, '='));

        await fileLogger.LogAsync($"📊 TOTALES:");
        await fileLogger.LogAsync($"   Posts analizados: {totalPosts:N0}");
        await fileLogger.LogAsync($"   Posts con FIDs: {postsWithFids:N0} ({(postsWithFids * 100.0 / totalPosts):F1}%)");
        await fileLogger.LogAsync($"   Posts con archivos: {postsWithFileNames:N0} ({(postsWithFileNames * 100.0 / totalPosts):F1}%)");

        await fileLogger.LogAsync($"\n🎯 FIDs:");
        await fileLogger.LogAsync($"   Total menciones: {totalFids:N0}");
        await fileLogger.LogAsync($"   FIDs únicos: {uniqueFids.Count:N0}");

        if (uniqueFids.Any())
        {
            var topFids = uniqueFids.OrderBy(f => f).Take(20);
            await fileLogger.LogAsync($"   Primeros 20: {string.Join(", ", topFids)}");
        }

        await fileLogger.LogAsync($"\n📁 ARCHIVOS:");
        await fileLogger.LogAsync($"   Total menciones: {totalFileNames:N0}");
        await fileLogger.LogAsync($"   Archivos únicos: {uniqueFileNames.Count:N0}");

        if (uniqueFileNames.Any())
        {
            var topFiles = uniqueFileNames.OrderBy(f => f).Take(10);
            await fileLogger.LogAsync($"   Primeros 10: {string.Join(", ", topFiles)}");
        }

        await fileLogger.LogAsync($"\n💡 RECOMENDACIÓN:");
        if (postsWithFids > postsWithFileNames)
        {
            await fileLogger.LogAsync($"   ✅ Priorizar migración por FID ({postsWithFids} posts)");
        }
        else if (postsWithFileNames > 0)
        {
            await fileLogger.LogAsync($"   ⚠️ Migración por nombre de archivo necesaria ({postsWithFileNames} posts)");
        }
        else
        {
            await fileLogger.LogAsync($"   ℹ️ Pocos posts con referencias de archivos");
        }
    }

    private async Task<List<DatosSimples>> GetFilesFromId(int id)
    {
        using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
        await connection.OpenAsync();
        // Query para obtener FIDs de la tabla de archivos
        var query = $@"select filename, fm.fid as fid
from file_usage fu 
inner join file_managed fm on fu.fid = fm.fid
where fu.id = @id";
        var fids = await connection.QueryAsync<DatosSimples>(query, new { id });
        return fids.ToList();
    }

    public class SimplePostData
    {
        public int WpPostId { get; set; }
        public int DrupalPostId { get; set; }
        public string PostTitle { get; set; }
        public string PostContent { get; set; }
        public DateTime PostDate { get; set; }
    }
    public class SimpleAnalysis
    {
        public List<string> Cadenas { get; set; } = new List<string>();
    }

    public class DatosSimples
    {
        public string FileName { get; set; }
        public int Fid { get; set; }
    }
}
