using System.Windows;

namespace UCrew.TTARCH2.GUI;

public partial class MainWindow
{
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == IsEnabledProperty && IsEnabled && IsLoaded)
            RefreshResourceCatalog();
    }
}
