using drupaltowp.Business;
using drupaltowp.ViewModels;

namespace drupaltowp.Services
{
    public class MigrationCoordinatorService
    {
        public VerificationBusiness VerificationBusiness { get; }
        public MigrationBusiness MigrationBusiness { get; }
        public PanopolyMigrationBusiness PanopolyMigrationBusiness { get; }
        public CancellationService CancellationService { get; }

        public OpinionMigrationBusiness OpinionMigrationBusiness { get; }
        public MigrationCoordinatorService(LoggerViewModel logger)
        {
            // Crear el servicio de cancelación primero
            CancellationService = new CancellationService();

            // Pasar el servicio de cancelación a los business que lo necesiten
            VerificationBusiness = new VerificationBusiness(logger);
            MigrationBusiness = new MigrationBusiness(logger);
            PanopolyMigrationBusiness = new PanopolyMigrationBusiness(logger, CancellationService);
            OpinionMigrationBusiness = new OpinionMigrationBusiness(logger, CancellationService);
        }

        /// <summary>
        /// Libera recursos del coordinador
        /// </summary>
        public void Dispose()
        {
            CancellationService?.Dispose();
        }
    }
}