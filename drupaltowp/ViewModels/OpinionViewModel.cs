using drupaltowp.Business;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace drupaltowp.ViewModels
{
    public class OpinionViewModel : INotifyPropertyChanged
    {
        private readonly OpinionMigrationBusiness _opinionBusiness;

        public OpinionViewModel(OpinionMigrationBusiness opinionMigrationBusiness)
        {
            _opinionBusiness = opinionMigrationBusiness;

            // Inicializar commands
            MigrateOpinionPagesCommand = new RelayCommand(async () => await _opinionBusiness.MigrateOpinionPagesAsync());
            LimpiarPublicacionesCommand = new RelayCommand(async () => await _opinionBusiness.LimpiarPublicacionesMigradasAsync());
            ArreglarImagenesCommand = new RelayCommand(async () => await _opinionBusiness.ArreglarImagenesAsync());
            CorregirTipoPublicacionCommand = new RelayCommand(async () => await _opinionBusiness.CorregirTipoPublicacionAsync());
        }

        #region Properties
        public static string PhaseTitle => "📄 FASE 4: MIGRACIÓN OPINION";
        public static string PhaseColor => "#E67E22";
        #endregion

        #region Commands
        public ICommand MigrateOpinionPagesCommand { get; }
        public ICommand LimpiarPublicacionesCommand { get; }
        public ICommand ArreglarImagenesCommand { get; }
        public ICommand CorregirTipoPublicacionCommand { get; }
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