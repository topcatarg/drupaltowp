using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using drupaltowp.Business;

namespace drupaltowp.ViewModels;

public class HubsPhaseViewModel : INotifyPropertyChanged
{
    private readonly HubsMigrationBusiness _hubsBusiness;

    public HubsPhaseViewModel(HubsMigrationBusiness hubsBusiness)
    {
        _hubsBusiness = hubsBusiness;

        // Inicializar commands
        AnalyzeHubsCommand = new RelayCommand(async () => await _hubsBusiness.AnalyzeHubsStructureAsync());
        MigrateHubsPublicationsCommand = new RelayCommand(async () => await _hubsBusiness.MigrateHubsPublicationsAsync());
        MigrateHubsImagesCommand = new RelayCommand(async () => await _hubsBusiness.MigrateHubsImagesAsync());
        ProcessHubsContentImagesCommand = new RelayCommand(async () => await _hubsBusiness.ProcessHubsContentImagesAsync());
        ShowHubsStatusCommand = new RelayCommand(async () => await _hubsBusiness.ShowHubsStatusAsync());
        ValidateHubsMigrationCommand = new RelayCommand(async () => await _hubsBusiness.ValidateHubsMigrationAsync());
        RollBackMigrationCommand = new RelayCommand(async () => await _hubsBusiness.RollBackMigrationAsync());
    }

    #region Properties
    public string PhaseTitle => "🌐 FASE 5: MIGRACIÓN HUBS";
    public string PhaseColor => "#3498DB";
    #endregion

    #region Commands
    public ICommand AnalyzeHubsCommand { get; }
    public ICommand MigrateHubsPublicationsCommand { get; }
    public ICommand MigrateHubsImagesCommand { get; }
    public ICommand ProcessHubsContentImagesCommand { get; }
    public ICommand ShowHubsStatusCommand { get; }
    public ICommand ValidateHubsMigrationCommand { get; }

    public ICommand RollBackMigrationCommand { get; }
    #endregion

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}