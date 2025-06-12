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

        public QuickControlsViewModel QuickControls =>
            new QuickControlsViewModel(_loggerViewModel, _coordinatorService.VerificationBusiness.ShowSystemStatusAsync);

        public LogPanelViewModel LogPanel =>
            new LogPanelViewModel(_loggerViewModel);
    }
}