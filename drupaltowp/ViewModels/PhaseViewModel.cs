using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace drupaltowp.ViewModels;

public class PhaseViewModel : INotifyPropertyChanged
{
    public string PhaseTitle { get; set; }
    public SolidColorBrush PhaseColor { get; set; }
    public ObservableCollection<PhaseButtonViewModel> PhaseButtons { get; set; }

    public PhaseViewModel()
    {
        PhaseButtons = new ObservableCollection<PhaseButtonViewModel>();
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class PhaseButtonViewModel
{
    public string ButtonText { get; set; }
    public SolidColorBrush ButtonColor { get; set; }
    public ICommand ButtonCommand { get; set; }
}