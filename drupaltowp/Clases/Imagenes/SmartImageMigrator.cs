using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using drupaltowp.Configuracion;
using drupaltowp.Models;
using drupaltowp.ViewModels;
using MySql.Data.MySqlClient;

namespace drupaltowp.Clases.Imagenes
{
    internal class SmartImageMigrator
    {
        private readonly LoggerViewModel _logger;
        private int? _genericImageId;

        public SmartImageMigrator(LoggerViewModel logger)
        {
            _logger = logger;
        }

        public async Task<ImageMigrationSummary> SmartMigrateImagesAsync()
        {
            var summary = new ImageMigrationSummary { StartTime = DateTime.Now};
            _logger.LogProcess("INICIANDO MIGRACION INTELIGENTE DE IMAGENES");
            _logger.LogInfo($"Solo post migrados y con fecha {ConfiguracionGeneral.FechaMinimaImagen.Year}");

            try
            {
                //Verifico la imagen generica
                await EnsureGenericImageExistsAsync();

                return summary;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en migracion inteligente: {ex.Message}");
                summary.EndTime = DateTime.Now;
                throw;
            }
        }

        private async Task EnsureGenericImageExistsAsync()
        {
            _logger.LogProcess("Verificando imagen generica...");

            using var connection = new MySqlConnection(ConfiguracionGeneral.WPconnectionString);
            await connection.OpenAsync();
            _genericImageId = await connection.QueryFirstOrDefaultAsync<int?>(
                "Select id from wp_posts where post_name = @slug and post_type = 'attachment'",
                new { slug = "generic-post-image" });
            if (_genericImageId.HasValue)
            {
                _logger.LogSuccess($"Imagen generica encontrada: ID {_genericImageId}");
                return;
            }
            var genericImagePathWP = Path.Combine(ConfiguracionGeneral.WPFileRoute, ConfiguracionGeneral.WPGenericImageFileName);
            var genericImagePathDrupal = Path.Combine(ConfiguracionGeneral.DrupalFileRoute, ConfiguracionGeneral.DrupalGenericImageFileName);
            if (!Directory.Exists(genericImagePathWP))
            {
                //La copio
                File.Copy(genericImagePathDrupal, genericImagePathWP );
            }
            _genericImageId = await CreateGenericImageRecordAsync(connection);
            _logger.LogSuccess($"Imagen generica creada: ID {_genericImageId}");
        }

        private async Task<int> CreateGenericImageRecordAsync(MySqlConnection connection)
        {
            return 0;
        }
    }
}
