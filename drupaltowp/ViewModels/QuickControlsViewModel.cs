using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using drupaltowp.Services;

namespace drupaltowp.ViewModels;

public class QuickControlsViewModel : INotifyPropertyChanged
{
    private readonly LoggerViewModel _loggerViewModel;
    private readonly Func<Task> _showStatusAction;
    private readonly CancellationService _cancellationService;

    public QuickControlsViewModel(LoggerViewModel loggerViewModel, Func<Task> showStatusAction, CancellationService cancellationService)
    {
        _loggerViewModel = loggerViewModel;
        _showStatusAction = showStatusAction;
        _cancellationService = cancellationService;

        // Suscribirse a eventos de cancelación
        _cancellationService.OperationStarted += OnOperationStarted;
        _cancellationService.OperationCancelled += OnOperationCancelled;
        _cancellationService.OperationCompleted += OnOperationCompleted;

        // Inicializar commands
        ClearLogCommand = new RelayCommand(ExecuteClearLog);
        ShowStatusCommand = new RelayCommand(async () => await ExecuteShowStatus());
        CancelOperationCommand = new RelayCommand(ExecuteCancelOperation, CanCancelOperation);
    }

    #region Properties
    private bool _isOperationInProgress;
    public bool IsOperationInProgress
    {
        get => _isOperationInProgress;
        private set
        {
            if (_isOperationInProgress != value)
            {
                _isOperationInProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CancelButtonText));
                // Forzar re-evaluación del comando
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private string _currentOperationName;
    public string CurrentOperationName
    {
        get => _currentOperationName;
        private set
        {
            if (_currentOperationName != value)
            {
                _currentOperationName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CancelButtonText));
            }
        }
    }

    public string CancelButtonText
    {
        get
        {
            if (IsOperationInProgress && !string.IsNullOrEmpty(CurrentOperationName))
            {
                return $"⏹️ Cancelar: {CurrentOperationName}";
            }
            return "⏹️ Cancelar Proceso";
        }
    }
    #endregion

    #region Commands
    public ICommand ClearLogCommand { get; }
    public ICommand ShowStatusCommand { get; }
    public ICommand CancelOperationCommand { get; }
    #endregion

    #region Command Implementations
    private void ExecuteClearLog()
    {
        _loggerViewModel.Clear();
        _loggerViewModel.LogInfo("🧹 Log limpiado");
    }

    private async Task ExecuteShowStatus()
    {
        try
        {
            _loggerViewModel.LogInfo("📊 Obteniendo estado del sistema...");
            await _showStatusAction?.Invoke();
        }
        catch (Exception ex)
        {
            _loggerViewModel.LogError($"Error mostrando estado: {ex.Message}");
        }
    }

    private void ExecuteCancelOperation()
    {
        try
        {
            if (_cancellationService.IsOperationInProgress)
            {
                _loggerViewModel.LogWarning($"⏹️ Cancelando operación: {_cancellationService.CurrentOperationName}");
                _cancellationService.CancelCurrentOperation();
            }
            else
            {
                _loggerViewModel.LogInfo("ℹ️ No hay operaciones en progreso para cancelar");
            }
        }
        catch (Exception ex)
        {
            _loggerViewModel.LogError($"Error cancelando operación: {ex.Message}");
        }
    }

    private bool CanCancelOperation()
    {
        return _cancellationService.IsOperationInProgress;
    }
    #endregion

    #region Event Handlers
    private void OnOperationStarted(object sender, string operationName)
    {
        App.Current?.Dispatcher.Invoke(() =>
        {
            IsOperationInProgress = true;
            CurrentOperationName = operationName;
            _loggerViewModel.LogProcess($"🚀 Iniciando: {operationName}");
        });
    }

    private void OnOperationCancelled(object sender, string operationName)
    {
        App.Current?.Dispatcher.Invoke(() =>
        {
            IsOperationInProgress = false;
            CurrentOperationName = null;
            _loggerViewModel.LogWarning($"⏹️ Operación cancelada: {operationName}");
        });
    }

    private void OnOperationCompleted(object sender, string operationName)
    {
        App.Current?.Dispatcher.Invoke(() =>
        {
            IsOperationInProgress = false;
            CurrentOperationName = null;
            _loggerViewModel.LogSuccess($"✅ Operación completada: {operationName}");
        });
    }
    #endregion

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion

    #region Cleanup
    public void Dispose()
    {
        _cancellationService.OperationStarted -= OnOperationStarted;
        _cancellationService.OperationCancelled -= OnOperationCancelled;
        _cancellationService.OperationCompleted -= OnOperationCompleted;
    }
    #endregion
}