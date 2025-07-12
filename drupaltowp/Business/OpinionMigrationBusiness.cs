using drupaltowp.Clases.Publicaciones.Opinion;
using drupaltowp.Configuracion;
using drupaltowp.Services;
using drupaltowp.ViewModels;
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

        /// <summary>
        /// Limpia todas las publicaciones Opinion migradas usando WordPress API
        /// </summary>
        public async Task LimpiarPublicacionesMigradasAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Limpiar Publicaciones Migradas",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("🧹 Iniciando limpieza de publicaciones Opinion...");

                    var cleaner = new OpinionPostCleaner(_logger, _wpClient)
                    {
                        // Configurar cancelación en el cleaner
                        Cancelar = false
                    };

                    // Monitorear cancelación en un task separado
                    var monitorTask = Task.Run(async () =>
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(500, cancellationToken);
                        }
                        cleaner.Cancelar = true;
                    }, cancellationToken);

                    try
                    {
                        await cleaner.LimpiarPublicacionesMigradasAsync(cancellationToken);
                        _logger.LogSuccess("✅ Limpieza de publicaciones completada");
                        _logger.LogInfo("🔄 Sistema listo para migración limpia desde cero");
                    }
                    finally
                    {
                        cleaner.Cancelar = true; // Asegurar que se detenga
                    }
                },
                timeoutMinutes: 30 // 30 minutos para limpieza
            );
        }

        /// <summary>
        /// Migra todas las páginas de tipo Opinion desde Drupal a WordPress
        /// </summary>
        public async Task MigrateOpinionPagesAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Migración Páginas Opinion",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("📄 Iniciando migración de páginas Opinion...");

                    var migrator = new OpinionPostMigrator(_logger, _wpClient)
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
                        await migrator.MigratePosts();
                        _logger.LogSuccess("✅ Migración de páginas Opinion completada");
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
        /// Arregla las imágenes rotas en los posts de Opinion ya migrados
        /// </summary>
        public async Task ArreglarImagenesAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Arreglar imagenes rotas",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("🖼️ Iniciando el arreglo de imágenes de Opinion...");

                    var migrator = new OpinionPostImageFixer(_logger, _wpClient)
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
                        await migrator.FixPostAsync();
                        _logger.LogSuccess("✅ Arreglo de imágenes de Opinion completado");
                    }
                    finally
                    {
                        migrator.Cancelar = true; // Asegurar que se detenga
                    }
                },
                timeoutMinutes: 60 // 1 hora para arreglo de imágenes
            );
        }

        /// <summary>
        /// Corrige el tipo de publicación de los posts de Opinion ya migrados
        /// Convierte posts normales con categoría "Opinion" al custom post type "opinion"
        /// </summary>
        public async Task CorregirTipoPublicacionAsync()
        {
            await _cancellationService.ExecuteOperationAsync(
                "Corregir Tipo de Publicación",
                async (cancellationToken) =>
                {
                    _logger.LogProcess("⚙️ Iniciando corrección de tipo de publicación...");

                    var corrector = new OpinionPostTypeCorrector(_logger)
                    {
                        // Configurar cancelación en el corrector
                        Cancelar = false
                    };

                    // Monitorear cancelación en un task separado
                    var monitorTask = Task.Run(async () =>
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(500, cancellationToken);
                        }
                        corrector.Cancelar = true;
                    }, cancellationToken);

                    try
                    {
                        await corrector.CorrectPostTypesAsync(cancellationToken);
                        _logger.LogSuccess("✅ Corrección de tipo de publicación completada");
                    }
                    finally
                    {
                        corrector.Cancelar = true; // Asegurar que se detenga
                    }
                },
                timeoutMinutes: 30 // 30 minutos para corrección
            );
        }
    }
}