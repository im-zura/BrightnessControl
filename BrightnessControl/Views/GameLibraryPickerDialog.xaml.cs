using System.IO;
using System.Windows;
using BrightnessControl.Models;
using BrightnessControl.Native;
using BrightnessControl.Services;

namespace BrightnessControl.Views;

/// <summary>
/// Primary game-picking flow: scans installed Steam/Epic libraries so the user doesn't need
/// the game running to build a profile. Falls back to the running-process picker for titles
/// from other sources (Battle.net, Xbox/Game Pass, standalone installs, etc.).
/// </summary>
public partial class GameLibraryPickerDialog : Window
{
    private List<DiscoveredGame> _allGames = new();

    /// <summary>The resolved executable name, e.g. "ForzaHorizon6.exe". Null if cancelled.</summary>
    public string? SelectedProcessName { get; private set; }

    /// <summary>Full path to the resolved executable, for icon extraction. Null if unresolved.</summary>
    public string? SelectedExePath { get; private set; }

    /// <summary>Friendly game name (e.g. "Forza Horizon 6") when picked from a library; null via process fallback.</summary>
    public string? SelectedDisplayName { get; private set; }

    public GameLibraryPickerDialog()
    {
        InitializeComponent();
        DwmInterop.ApplyModernChrome(this, BackdropType.None);
        Loaded += (_, _) => Rescan();
    }

    private async void Rescan()
    {
        SetBusy(true);
        _allGames = await Task.Run(GameLibraryScanner.ScanAll);
        SetBusy(false);

        GameList.ItemsSource = _allGames;
        EmptyStateText.Visibility = _allGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        RescanButton.IsEnabled = !busy;
        SelectButton.IsEnabled = !busy;
        GameList.IsEnabled = !busy;
        ValidationText.Visibility = Visibility.Collapsed;
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var filter = FilterBox.Text.Trim();
        GameList.ItemsSource = string.IsNullOrEmpty(filter)
            ? _allGames
            : _allGames.Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void RescanButton_Click(object sender, RoutedEventArgs e) => Rescan();

    private void GameList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Commit();

    private void SelectButton_Click(object sender, RoutedEventArgs e) => Commit();

    private async void Commit()
    {
        if (GameList.SelectedItem is not DiscoveredGame game)
            return;

        SetBusy(true);
        var exePath = await Task.Run(() => GameLibraryScanner.ResolveExecutable(game));
        SetBusy(false);

        if (exePath == null)
        {
            ValidationText.Text = $"Couldn't find an executable for \"{game.Name}\". Try picking it from running processes instead.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        SelectedProcessName = Path.GetFileName(exePath);
        SelectedExePath = exePath;
        SelectedDisplayName = game.Name;
        DialogResult = true;
    }

    private void RunningProcessButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerDialog { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedProcessName != null)
        {
            SelectedProcessName = picker.SelectedProcessName;
            SelectedExePath = picker.SelectedExePath;
            DialogResult = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
