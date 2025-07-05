using drupaltowp.Services;

namespace drupaltowp.ViewModels
{
    public class ViewModelLocator
    {
        private readonly MigrationCoordinatorService _coordinatorService;
        private readonly LoggerViewModel _loggerViewModel;

        public ViewModelLocator(MigrationCoordinatorService coordinatorService, LoggerViewModel loggerViewModel)
        {
            _coordinatorService = coordinatorService;
            _loggerViewModel = loggerViewModel;
        }

        public VerificationPhaseViewModel VerificationPhase =>
            new VerificationPhaseViewModel(_coordinatorService.VerificationBusiness);

        public PanopolyPhaseViewModel PanopolyPhase =>
            new PanopolyPhaseViewModel(_coordinatorService.PanopolyMigrationBusiness);

        public OpinionViewModel OpinionPhase =>
            new OpinionViewModel(_coordinatorService.OpinionMigrationBusiness);

        public HubsPhaseViewModel HubsPhase =>
            new HubsPhaseViewModel(_coordinatorService.HubsMigrationBusiness);
        public QuickControlsViewModel QuickControls =>
            new QuickControlsViewModel(
                _loggerViewModel,
                _coordinatorService.VerificationBusiness.ShowSystemStatusAsync,
                _coordinatorService.CancellationService);

        public LogPanelViewModel LogPanel =>
            new LogPanelViewModel(_loggerViewModel);

        /// <summary>
        /// Libera recursos
        /// </summary>
        public void Dispose()
        {
            QuickControls?.Dispose();
            _coordinatorService?.Dispose();
        }
    }
}