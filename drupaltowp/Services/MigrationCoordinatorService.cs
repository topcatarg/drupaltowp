using drupaltowp.Business;
using drupaltowp.ViewModels;

namespace drupaltowp.Services
{
    public class MigrationCoordinatorService
    {
        public VerificationBusiness VerificationBusiness { get; }
        public MigrationBusiness MigrationBusiness { get; }

        public MigrationCoordinatorService(LoggerViewModel logger)
        {
            VerificationBusiness = new VerificationBusiness(logger);
            MigrationBusiness = new MigrationBusiness(logger);
        }
    }
}