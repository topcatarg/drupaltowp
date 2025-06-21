using drupaltowp.Business;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
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
            ArreglarImagenesCommand = new RelayCommand(async () => await _opinionBusiness.ArreglarImagenesAsync());
        }

        #region Properties
        public static string PhaseTitle => "📄 FASE 4: MIGRACIÓN OPINION";
        public static string PhaseColor => "#E67E22";
        #endregion

        #region Commands
        public ICommand MigrateOpinionPagesCommand { get; }

        public ICommand ArreglarImagenesCommand { get; }


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
