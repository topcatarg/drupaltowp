using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;
using System;
using System.Threading.Tasks;
using WordPressPCL;

namespace drupaltowp.Business
{
    public class VerificationBusiness
    {
        private readonly LoggerViewModel _logger;

        public VerificationBusiness(LoggerViewModel logger)
        {
            _logger = logger;
        }

        public async Task<bool> CheckPrerequisitesAsync()
        {
            try
            {
                _logger.LogProcess("🔍 Verificando prerrequisitos del sistema...");

                // Verificar conexión a WordPress
                await CheckWordPressConnectionAsync();

                // Verificar conexión a Drupal
                await CheckDrupalConnectionAsync();

                // Verificar mapeos existentes
                await CheckExistingMappingsAsync();

                _logger.LogSuccess("🎉 Todos los prerrequisitos están OK");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error en prerrequisitos: {ex.Message}");
                return false;
            }
        }

        public async Task ShowSystemStatusAsync()
        {
            try
            {
                _logger.LogProcess("📊 Obteniendo estado del sistema...");

                // Verificar WordPress
                var wpStatus = await GetWordPressStatusAsync();
                _logger.LogInfo($"📊 WordPress: {(wpStatus ? "✅ Conectado" : "❌ Error")}");

                // Verificar Drupal
                var drupalStatus = await GetDrupalStatusAsync();
                _logger.LogInfo($"📊 Drupal: {(drupalStatus ? "✅ Conectado" : "❌ Error")}");

                // Contar elementos migrados
                await ShowMigrationCountsAsync();

                _logger.LogSuccess("✅ Estado del sistema verificado");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error verificando estado: {ex.Message}");
                throw;
            }
        }

        public async Task AnalyzeDatabaseAsync()
        {
            try
            {
                _logger.LogProcess("🔍 Analizando estructura de base de datos...");

                var analyzer = new DrupalAnalyzer(
                    ConfiguracionGeneral.DrupalconnectionString,
                    null, // No necesitamos TextBlock aquí
                    null  // No necesitamos ScrollViewer aquí
                );

                _logger.LogInfo("🔍 Conectando a base de datos de Drupal...");

                // Analizar tipos de contenido
                _logger.LogInfo("📊 Analizando tipos de contenido...");
                await AnalyzeContentTypesAsync();

                _logger.LogInfo("📋 Analizando estructura de campos...");
                await AnalyzeFieldStructureAsync();

                _logger.LogSuccess("✅ Análisis de base de datos completado");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error analizando BD: {ex.Message}");
                throw;
            }
        }

        #region Private Methods
        private async Task CheckWordPressConnectionAsync()
        {
            _logger.LogInfo("✅ Verificando conexión a WordPress...");

            var wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
            wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);

            // Intentar obtener usuarios para verificar conexión
            var users = await wpClient.Users.GetAllAsync();
            _logger.LogInfo($"   📊 WordPress usuarios encontrados: {users.Count}");
        }

        private async Task CheckDrupalConnectionAsync()
        {
            _logger.LogInfo("✅ Verificando conexión a Drupal...");

            using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
            await connection.OpenAsync();

            // Verificar algunas tablas básicas
            var nodeCount = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM node");
            _logger.LogInfo($"   📊 Drupal nodos encontrados: {nodeCount}");
        }

        private async Task CheckExistingMappingsAsync()
        {
            _logger.LogInfo("✅ Verificando mapeos existentes...");

            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            var userMappings = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM user_mapping");
            var categoryMappings = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM category_mapping");

            _logger.LogInfo($"   📊 Mapeos usuarios: {userMappings}");
            _logger.LogInfo($"   📊 Mapeos categorías: {categoryMappings}");
        }

        private async Task<bool> GetWordPressStatusAsync()
        {
            try
            {
                var wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
                wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);
                await wpClient.Users.GetAllAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> GetDrupalStatusAsync()
        {
            try
            {
                using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
                await connection.OpenAsync();
                await connection.QueryFirstOrDefaultAsync<int>("SELECT 1");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task ShowMigrationCountsAsync()
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();

            try
            {
                var userCount = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM user_mapping");
                var categoryCount = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM category_mapping");
                var tagCount = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM tag_mapping");
                var postCount = await connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM post_mapping_biblioteca");

                _logger.LogInfo($"   📊 Usuarios migrados: {userCount}");
                _logger.LogInfo($"   📊 Categorías migradas: {categoryCount}");
                _logger.LogInfo($"   📊 Tags migrados: {tagCount}");
                _logger.LogInfo($"   📊 Posts migrados: {postCount}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"   ⚠️ Error obteniendo contadores: {ex.Message}");
            }
        }

        private async Task AnalyzeContentTypesAsync()
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
            await connection.OpenAsync();

            var contentTypes = await connection.QueryAsync<dynamic>(@"
                SELECT type, COUNT(*) as cantidad 
                FROM node 
                GROUP BY type 
                ORDER BY cantidad DESC");

            foreach (var type in contentTypes)
            {
                _logger.LogInfo($"   📋 {type.type}: {type.cantidad} nodos");
            }
        }

        private async Task AnalyzeFieldStructureAsync()
        {
            using var connection = new MySqlConnection(ConfiguracionGeneral.DrupalconnectionString);
            await connection.OpenAsync();

            var fieldTables = await connection.QueryAsync<dynamic>(@"
                SELECT TABLE_NAME, TABLE_ROWS
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_SCHEMA = DATABASE() 
                AND TABLE_NAME LIKE 'field_data_%'
                ORDER BY TABLE_ROWS DESC
                LIMIT 10");

            _logger.LogInfo("   📋 Principales tablas de campos:");
            foreach (var table in fieldTables)
            {
                _logger.LogInfo($"      {table.TABLE_NAME}: {table.TABLE_ROWS} registros");
            }
        }
        #endregion
    }
}