using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using drupaltowp.Business;

namespace drupaltowp.ViewModels
{
    public class VerificationPhaseViewModel : INotifyPropertyChanged
    {
        private readonly VerificationBusiness _verificationBusiness;

        public VerificationPhaseViewModel(VerificationBusiness verificationBusiness)
        {
            _verificationBusiness = verificationBusiness;

            // Inicializar commands
            CheckPrerequisitesCommand = new RelayCommand(async () => await _verificationBusiness.CheckPrerequisitesAsync());
            ShowStatusCommand = new RelayCommand(async () => await _verificationBusiness.ShowSystemStatusAsync());
            AnalyzeDatabaseCommand = new RelayCommand(async () => await _verificationBusiness.AnalyzeDatabaseAsync());
        }

        #region Properties
        public string PhaseTitle => "🔍 FASE 1: VERIFICACIÓN";
        public string PhaseColor => "#3498DB";
        #endregion

        #region Commands
        public ICommand CheckPrerequisitesCommand { get; }
        public ICommand ShowStatusCommand { get; }
        public ICommand AnalyzeDatabaseCommand { get; }
        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}