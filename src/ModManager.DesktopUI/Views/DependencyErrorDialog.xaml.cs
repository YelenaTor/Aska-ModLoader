using MahApps.Metro.Controls;
using ModManager.DesktopUI.Models;
using System.Collections.Generic;
using System.Windows;

namespace ModManager.DesktopUI.Views;

/// <summary>
/// Interaction logic for DependencyErrorDialog.xaml
/// </summary>
public partial class DependencyErrorDialog : MetroWindow
{
    public DependencyErrorDialog(string modName, IEnumerable<ModDependencyDisplayModel> dependencies)
    {
        InitializeComponent();
        ModNameText.Text = modName;
        DependencyList.ItemsSource = dependencies;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
