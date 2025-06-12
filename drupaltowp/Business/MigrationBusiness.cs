using System;
using System.Threading.Tasks;
using drupaltowp.Configuracion;
using drupaltowp.ViewModels;
using WordPressPCL;

namespace drupaltowp.Business
{
    public class MigrationBusiness
    {
        private readonly LoggerViewModel _logger;

        public MigrationBusiness(LoggerViewModel logger)
        {
            _logger = logger;
        }

        public async Task MigrateUsersAsync()
        {
            try
            {
                _logger.LogProcess("👥 Iniciando migración de usuarios...");

                var wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
                wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);

                var migrator = new UserMigratorWPF(
                    ConfiguracionGeneral.DrupalconnectionString,
                    ConfiguracionGeneral.WPconnectionString,
                    wpClient,
                    null, // No necesitamos TextBlock
                    null  // No necesitamos ScrollViewer
                );

                // Tu lógica de migración aquí
                _logger.LogSuccess("✅ Usuarios migrados correctamente");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error migrando usuarios: {ex.Message}");
                throw;
            }
        }

        public async Task MigrateCategoriesAsync()
        {
            try
            {
                _logger.LogProcess("📂 Iniciando migración de categorías...");
                // Tu lógica aquí
                _logger.LogSuccess("✅ Categorías migradas correctamente");
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Error migrando categorías: {ex.Message}");
                throw;
            }
        }
    }
}