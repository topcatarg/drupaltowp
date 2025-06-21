using drupaltowp.Clases.Publicaciones.Opinion;
using drupaltowp.Clases.Publicaciones.Panopoly;
using drupaltowp.Configuracion;
using drupaltowp.Services;
using drupaltowp.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WordPressPCL;

namespace drupaltowp.Business
{
    public class OpinionMigrationBusiness
    {
        private readonly LoggerViewModel _logger;
        private readonly WordPressClient _wpClient;
        private readonly CancellationService _cancellationService;

        public OpinionMigrationBusiness(LoggerViewModel logger, CancellationService cancellationService)
        {
            _logger = logger;
            _cancellationService = cancellationService;
            _wpClient = new WordPressClient(ConfiguracionGeneral.UrlsitioWP);
            _wpClient.Auth.UseBasicAuth(ConfiguracionGeneral.Usuario, ConfiguracionGeneral.Password);
        }

        public async Task MigrateOpinionPagesAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Migración Páginas Opinion",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("📄 Iniciando migración de páginas Opinion...");

                    var migrator = new OpinionPostMigrator(_logger, _wpClient);

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
                        await migrator.MigratePosts();
                        //_logger.LogSuccess($"✅ Páginas Opinion migradas: {migratedPosts.Count:N0}");
                    }
                    finally
                    {
                        migrator.Cancelar = true; // Asegurar que se detenga
                    }
                },
                timeoutMinutes: 60 // 1 hora para migración completa
            );
        }

        public async Task ArreglarImagenesAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Arreglar imagenes rotas",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("📄 Iniciando el arreglo de imagenes de Opinion...");

                    var migrator = new OpinionPostImageFixer(_logger, _wpClient);

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
                        await migrator.FixPostAsync();
                        //_logger.LogSuccess($"✅ Páginas Opinion migradas: {migratedPosts.Count:N0}");
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
}
