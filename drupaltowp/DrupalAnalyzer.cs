using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using Dapper;
using MySql.Data.MySqlClient;
using drupaltowp.Models;

namespace drupaltowp
{
    public class DrupalAnalyzer
    {
        private readonly string _drupalConnectionString;
        private readonly TextBlock _statusTextBlock;
        private readonly ScrollViewer _logScrollViewer;

        public DrupalAnalyzer(string drupalConnectionString, TextBlock statusTextBlock, ScrollViewer logScrollViewer)
        {
            _drupalConnectionString = drupalConnectionString;
            _statusTextBlock = statusTextBlock;
            _logScrollViewer = logScrollViewer;
        }

        public async Task AnalyzeDrupalPostsAsync()
        {
            LogMessage("🔍 ANALIZANDO ESTRUCTURA DE POSTS EN DRUPAL...");

            try
            {
                using var connection = new MySqlConnection(_drupalConnectionString);
                await connection.OpenAsync();

                // 1. Analizar tipos de contenido
                LogMessage("📊 Tipos de contenido:");
                var contentTypes = await connection.QueryAsync<dynamic>(@"
                    SELECT type, COUNT(*) as cantidad 
                    FROM node 
                    GROUP BY type 
                    ORDER BY cantidad DESC");

                foreach (var type in contentTypes)
                {
                    LogMessage($"   - {type.type}: {type.cantidad} nodos");
                }

                // 2. Verificar campos de imagen
                await AnalyzeImageFieldsAsync(connection);

                // 3. Analizar posts de ejemplo
                await AnalyzeSamplePostsAsync(connection);

                LogMessage("✅ Análisis completado.");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error en análisis: {ex.Message}");
                throw;
            }
        }

        public async Task<PostAnalysisResult> GetPostAnalysisAsync()
        {
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();

            var result = new PostAnalysisResult();

            LogMessage("📊 Calculando estadísticas de posts...");

            // Contar posts por tipo
            var postCounts = await connection.QueryAsync<dynamic>(@"
                SELECT type, COUNT(*) as count 
                FROM node 
                WHERE type IN ('article', 'blog', 'story')
                GROUP BY type");

            result.PostCountsByType = postCounts.ToDictionary(x => (string)x.type, x => (int)x.count);

            // Contar posts con imágenes (usando field_data_field_featured_image)
            try
            {
                result.PostsWithFeaturedImage = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT COUNT(DISTINCT n.nid)
                    FROM node n
                    JOIN field_data_field_featured_image img ON n.nid = img.entity_id
                    WHERE n.type IN ('article', 'blog', 'story')
                    AND img.field_featured_image_fid IS NOT NULL
                    AND img.field_featured_image_fid > 0");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Error contando posts con imagen: {ex.Message}");
                result.PostsWithFeaturedImage = 0;
            }

            // Contar posts con bajadas
            try
            {
                result.PostsWithBajada = await connection.QueryFirstOrDefaultAsync<int>(@"
                    SELECT COUNT(DISTINCT n.nid)
                    FROM node n
                    JOIN field_data_field_bajada bj ON n.nid = bj.entity_id
                    WHERE n.type IN ('article', 'blog', 'story')
                    AND bj.field_bajada_value IS NOT NULL
                    AND bj.field_bajada_value != ''");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Error contando posts con bajada: {ex.Message}");
                result.PostsWithBajada = 0;
            }

            // Total de posts
            result.TotalPosts = await connection.QueryFirstOrDefaultAsync<int>(@"
                SELECT COUNT(*) 
                FROM node 
                WHERE type IN ('article', 'blog', 'story')");

            LogMessage($"📈 Estadísticas calculadas:");
            LogMessage($"   Total posts: {result.TotalPosts}");
            LogMessage($"   Con imagen destacada: {result.PostsWithFeaturedImage}");
            LogMessage($"   Con bajada: {result.PostsWithBajada}");

            return result;
        }

        public async Task AnalyzeAllContentTypesAsync()
        {
            LogMessage("🔍 ANALIZANDO TODOS LOS TIPOS DE CONTENIDO...");

            try
            {
                using var connection = new MySqlConnection(_drupalConnectionString);
                await connection.OpenAsync();

                var contentTypes = new[]
                {
                    "biblioteca", "panopoly_page", "agenda", "newsletter",
                    "simpleads", "videos", "opinion", "hubs", "page", "webform"
                };

                foreach (var contentType in contentTypes)
                {
                    await AnalyzeSpecificContentTypeAsync(connection, contentType);
                    LogMessage(""); // Línea en blanco entre tipos
                }

                LogMessage("✅ Análisis de todos los tipos completado.");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error en análisis: {ex.Message}");
                throw;
            }
        }

        public async Task AnalyzeSpecificContentTypeAsync(MySqlConnection connection, string contentType)
        {
            LogMessage($"📋 ANALIZANDO TIPO: {contentType.ToUpper()}");

            try
            {
                // 1. Contar nodos de este tipo
                var nodeCount = await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM node WHERE type = @type", new { type = contentType });
                LogMessage($"   📊 Total de nodos: {nodeCount}");

                if (nodeCount == 0)
                {
                    LogMessage($"   ⚠️ No hay nodos de tipo '{contentType}'");
                    return;
                }

                // 2. Analizar campos específicos
                await AnalyzeContentTypeFieldsAsync(connection, contentType);

                // 3. Mostrar ejemplos
                await ShowContentTypeExamplesAsync(connection, contentType);

                // 4. Analizar estado de publicación
                await AnalyzeContentTypeStatusAsync(connection, contentType);
            }
            catch (Exception ex)
            {
                LogMessage($"   ❌ Error analizando {contentType}: {ex.Message}");
            }
        }

        private async Task AnalyzeContentTypeFieldsAsync(MySqlConnection connection, string contentType)
        {
            LogMessage($"   🔍 Campos disponibles para {contentType}:");

            try
            {
                // Buscar todas las tablas field_data que podrían contener campos de este tipo
                var fieldTables = await connection.QueryAsync<dynamic>(@"
                    SELECT 
                        TABLE_NAME,
                        COUNT(*) as record_count
                    FROM INFORMATION_SCHEMA.TABLES t
                    LEFT JOIN (
                        SELECT 
                            CASE 
                                WHEN TABLE_NAME = 'field_data_body' THEN (SELECT COUNT(*) FROM field_data_body WHERE bundle = @contentType)
                                WHEN TABLE_NAME = 'field_data_field_bajada' THEN (SELECT COUNT(*) FROM field_data_field_bajada WHERE bundle = @contentType)
                                WHEN TABLE_NAME = 'field_data_field_featured_image' THEN (SELECT COUNT(*) FROM field_data_field_featured_image WHERE bundle = @contentType)
                                WHEN TABLE_NAME = 'field_data_field_imagen' THEN (SELECT COUNT(*) FROM field_data_field_imagen WHERE bundle = @contentType)
                                WHEN TABLE_NAME = 'field_data_field_fecha' THEN (SELECT COUNT(*) FROM field_data_field_fecha WHERE bundle = @contentType)
                                WHEN TABLE_NAME = 'field_data_field_autor' THEN (SELECT COUNT(*) FROM field_data_field_autor WHERE bundle = @contentType)
                                WHEN TABLE_NAME = 'field_data_field_video_url' THEN (SELECT COUNT(*) FROM field_data_field_video_url WHERE bundle = @contentType)
                                WHEN TABLE_NAME = 'field_data_field_archivo' THEN (SELECT COUNT(*) FROM field_data_field_archivo WHERE bundle = @contentType)
                                WHEN TABLE_NAME = 'field_data_field_descripcion' THEN (SELECT COUNT(*) FROM field_data_field_descripcion WHERE bundle = @contentType)
                                ELSE 0
                            END as record_count,
                            TABLE_NAME
                        FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_SCHEMA = DATABASE() 
                        AND TABLE_NAME LIKE 'field_data_%'
                    ) counts ON t.TABLE_NAME = counts.TABLE_NAME
                    WHERE t.TABLE_SCHEMA = DATABASE() 
                    AND t.TABLE_NAME LIKE 'field_data_%'
                    AND counts.record_count > 0
                    ORDER BY counts.record_count DESC", new { contentType });

                // Método más simple: verificar campos comunes uno por uno
                await CheckCommonFieldsAsync(connection, contentType);
            }
            catch (Exception ex)
            {
                LogMessage($"   ❌ Error analizando campos: {ex.Message}");
            }
        }

        private async Task CheckCommonFieldsAsync(MySqlConnection connection, string contentType)
        {
            var commonFields = new Dictionary<string, string>
            {
                ["body"] = "field_data_body",
                ["bajada"] = "field_data_field_bajada",
                ["featured_image"] = "field_data_field_featured_image",
                ["imagen"] = "field_data_field_imagen",
                ["fecha"] = "field_data_field_fecha",
                ["autor"] = "field_data_field_autor",
                ["video_url"] = "field_data_field_video_url",
                ["archivo"] = "field_data_field_archivo",
                ["descripcion"] = "field_data_field_descripcion",
                ["tags"] = "field_data_field_tags",
                ["categorias"] = "field_data_field_categories",
                ["panopoly_categories"] = "field_data_panopoly_categories",
                ["biblioteca_categorias"] = "field_data_bibliteca_categorias"
            };

            foreach (var field in commonFields)
            {
                try
                {
                    var count = await connection.QueryFirstOrDefaultAsync<int>(
                        $"SELECT COUNT(*) FROM {field.Value} WHERE bundle = @contentType",
                        new { contentType });

                    if (count > 0)
                    {
                        LogMessage($"      ✓ {field.Key}: {count} registros");
                    }
                }
                catch
                {
                    // Campo no existe o error - ignorar
                }
            }
        }

        private async Task ShowContentTypeExamplesAsync(MySqlConnection connection, string contentType)
        {
            LogMessage($"   📝 Ejemplos de {contentType}:");

            try
            {
                var examples = await connection.QueryAsync<dynamic>(@"
                    SELECT 
                        n.nid,
                        n.title,
                        n.uid,
                        n.status,
                        n.created,
                        CASE WHEN b.body_value IS NOT NULL THEN 'SÍ' ELSE 'NO' END as tiene_body
                    FROM node n
                    LEFT JOIN field_data_body b ON n.nid = b.entity_id AND b.bundle = @contentType
                    WHERE n.type = @contentType
                    ORDER BY n.created DESC
                    LIMIT 3", new { contentType });

                foreach (var example in examples)
                {
                    LogMessage($"      📄 [{example.nid}] {example.title}");
                    LogMessage($"         Usuario: {example.uid}, Estado: {example.status}, Body: {example.tiene_body}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"   ❌ Error mostrando ejemplos: {ex.Message}");
            }
        }

        private async Task AnalyzeContentTypeStatusAsync(MySqlConnection connection, string contentType)
        {
            try
            {
                var statusStats = await connection.QueryAsync<dynamic>(@"
                    SELECT 
                        status,
                        COUNT(*) as count
                    FROM node 
                    WHERE type = @contentType
                    GROUP BY status", new { contentType });

                LogMessage($"   📊 Estado de publicación:");
                foreach (var stat in statusStats)
                {
                    var statusText = stat.status == 1 ? "Publicado" : "No publicado";
                    LogMessage($"      {statusText}: {stat.count}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"   ❌ Error analizando estado: {ex.Message}");
            }
        }

        private async Task AnalyzeImageFieldsAsync(MySqlConnection connection)
        {
            LogMessage("\n🔍 Analizando campos de imagen:");

            // Verificar field_data_field_imagen (tabla antigua)
            var imagenCount = 0;
            try
            {
                imagenCount = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM field_data_field_imagen");
            }
            catch
            {
                // Tabla no existe
            }
            LogMessage($"   - field_data_field_imagen: {imagenCount} registros");

            // Verificar field_data_field_featured_image (tabla correcta)
            var featuredCount = 0;
            try
            {
                featuredCount = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM field_data_field_featured_image");
            }
            catch
            {
                // Tabla no existe
            }
            LogMessage($"   - field_data_field_featured_image: {featuredCount} registros");

            // Mostrar estructura de la tabla que usamos (field_data_field_featured_image)
            if (featuredCount > 0)
            {
                LogMessage("\n📋 Estructura de field_data_field_featured_image (TABLA QUE USAMOS):");
                try
                {
                    var featuredStructure = await connection.QueryAsync<dynamic>(@"
                        SELECT entity_id, field_featured_image_fid, field_featured_image_alt, field_featured_image_title 
                        FROM field_data_field_featured_image 
                        LIMIT 3");

                    foreach (var row in featuredStructure)
                    {
                        LogMessage($"   Ejemplo: entity_id={row.entity_id}, fid={row.field_featured_image_fid}");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"   Error leyendo estructura: {ex.Message}");
                }
            }
            else
            {
                LogMessage("   ⚠️ ADVERTENCIA: No se encontraron registros en field_data_field_featured_image");
                LogMessage("   ℹ️ Los posts no tendrán imágenes destacadas");
            }
        }

        private async Task AnalyzeAllImageTablesAsync(MySqlConnection connection)
        {
            LogMessage("\n🔍 Buscando todas las tablas de imagen:");
            try
            {
                var imageTables = await connection.QueryAsync<dynamic>(@"
                    SELECT TABLE_NAME, TABLE_ROWS
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_SCHEMA = DATABASE() 
                    AND (TABLE_NAME LIKE '%field%image%' 
                         OR TABLE_NAME LIKE '%field%foto%' 
                         OR TABLE_NAME LIKE '%field%picture%'
                         OR TABLE_NAME LIKE '%field%featured%')
                    ORDER BY TABLE_ROWS DESC");

                foreach (var table in imageTables)
                {
                    LogMessage($"   - {table.TABLE_NAME}: {table.TABLE_ROWS} registros");
                }

                if (!imageTables.Any())
                {
                    LogMessage("   ⚠️ No se encontraron tablas de imagen");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"   Error buscando tablas: {ex.Message}");
            }
        }

        private async Task AnalyzeSamplePostsAsync(MySqlConnection connection)
        {
            LogMessage("\n📝 Posts de ejemplo:");

            var samplePosts = await connection.QueryAsync<dynamic>(@"
                SELECT 
                    n.nid,
                    n.title,
                    n.type,
                    n.uid,
                    n.status,
                    CASE WHEN b.body_value IS NOT NULL THEN 'SÍ' ELSE 'NO' END as tiene_body,
                    CASE WHEN bj.field_bajada_value IS NOT NULL THEN 'SÍ' ELSE 'NO' END as tiene_bajada
                FROM node n
                LEFT JOIN field_data_body b ON n.nid = b.entity_id
                LEFT JOIN field_data_field_bajada bj ON n.nid = bj.entity_id
                WHERE n.type IN ('article', 'blog', 'story')
                ORDER BY n.created DESC
                LIMIT 5");

            foreach (var post in samplePosts)
            {
                LogMessage($"   📄 [{post.nid}] {post.title}");
                LogMessage($"      Tipo: {post.type}, Usuario: {post.uid}, Body: {post.tiene_body}, Bajada: {post.tiene_bajada}");
            }
        }

        private async Task AnalyzeVocabulariesAsync(MySqlConnection connection)
        {
            LogMessage("\n📚 Vocabularios de taxonomía:");
            try
            {
                var vocabularies = await connection.QueryAsync<dynamic>(@"
                    SELECT v.name, v.machine_name, COUNT(t.tid) as term_count
                    FROM taxonomy_vocabulary v
                    LEFT JOIN taxonomy_term_data t ON v.vid = t.vid
                    GROUP BY v.vid, v.name, v.machine_name
                    ORDER BY term_count DESC");

                foreach (var vocab in vocabularies)
                {
                    LogMessage($"   - {vocab.name} ({vocab.machine_name}): {vocab.term_count} términos");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"   Error analizando vocabularios: {ex.Message}");
            }
        }

        #region Analizadores Específicos por Tipo

        public async Task AnalyzeBibliotecaAsync()
        {
            LogMessage("📚 ANALIZANDO TIPO: BIBLIOTECA");
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();
            await AnalyzeSpecificContentTypeAsync(connection, "biblioteca");
        }

        public async Task AnalyzePanopolyPageAsync()
        {
            LogMessage("📄 ANALIZANDO TIPO: PANOPOLY_PAGE");
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();
            await AnalyzeSpecificContentTypeAsync(connection, "panopoly_page");
        }

        public async Task AnalyzeAgendaAsync()
        {
            LogMessage("📅 ANALIZANDO TIPO: AGENDA");
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();
            await AnalyzeSpecificContentTypeAsync(connection, "agenda");
        }

        public async Task AnalyzeNewsletterAsync()
        {
            LogMessage("📰 ANALIZANDO TIPO: NEWSLETTER");
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();
            await AnalyzeSpecificContentTypeAsync(connection, "newsletter");
        }

        public async Task AnalyzeSimpleadsAsync()
        {
            LogMessage("📢 ANALIZANDO TIPO: SIMPLEADS");
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();
            await AnalyzeSpecificContentTypeAsync(connection, "simpleads");
        }

        public async Task AnalyzeVideosAsync()
        {
            LogMessage("🎥 ANALIZANDO TIPO: VIDEOS");
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();
            await AnalyzeSpecificContentTypeAsync(connection, "videos");
        }

        public async Task AnalyzeOpinionAsync()
        {
            LogMessage("💭 ANALIZANDO TIPO: OPINION");
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();
            await AnalyzeSpecificContentTypeAsync(connection, "opinion");
        }

        public async Task AnalyzeHubsAsync()
        {
            LogMessage("🌐 ANALIZANDO TIPO: HUBS");
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();
            await AnalyzeSpecificContentTypeAsync(connection, "hubs");
        }

        public async Task AnalyzePageAsync()
        {
            LogMessage("📃 ANALIZANDO TIPO: PAGE");
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();
            await AnalyzeSpecificContentTypeAsync(connection, "page");
        }

        public async Task AnalyzeWebformAsync()
        {
            LogMessage("📝 ANALIZANDO TIPO: WEBFORM");
            using var connection = new MySqlConnection(_drupalConnectionString);
            await connection.OpenAsync();
            await AnalyzeSpecificContentTypeAsync(connection, "webform");
        }

        #endregion

        public async Task AnalyzeDatabaseStructureAsync()
        {
            LogMessage("🔍 ANALIZANDO ESTRUCTURA COMPLETA DE LA BASE DE DATOS...");

            try
            {
                using var connection = new MySqlConnection(_drupalConnectionString);
                await connection.OpenAsync();

                // 1. Analizar todas las tablas relacionadas con fields
                LogMessage("\n📋 Tablas de campos (fields):");
                var fieldTables = await connection.QueryAsync<dynamic>(@"
                    SELECT TABLE_NAME, TABLE_ROWS
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_SCHEMA = DATABASE() 
                    AND TABLE_NAME LIKE 'field_data_%'
                    ORDER BY TABLE_ROWS DESC");

                foreach (var table in fieldTables)
                {
                    LogMessage($"   - {table.TABLE_NAME}: {table.TABLE_ROWS} registros");
                }

                // 2. Analizar tablas de imagen específicamente
                await AnalyzeAllImageTablesAsync(connection);

                // 3. Analizar vocabularios de taxonomía
                await AnalyzeVocabulariesAsync(connection);

                LogMessage("✅ Análisis de estructura completado.");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Error en análisis de estructura: {ex.Message}");
                throw;
            }
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}\n";

            _statusTextBlock.Dispatcher.Invoke(() =>
            {
                _statusTextBlock.Text += logEntry;
                _logScrollViewer?.ScrollToBottom();
            });
        }
    }
}