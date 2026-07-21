using System.Diagnostics;
using System.Windows;
using BrightnessControl.Native;

namespace BrightnessControl.Views;

internal sealed record ProcessEntry(string ExeName, string WindowTitle, string? ExePath);

/// <summary>
/// Lets the user pick a running process to seed a game profile. Deliberately only reads
/// Process.ProcessName/MainWindowTitle, never MainModule — the latter throws Win32Exception
/// for anti-cheat-protected processes when this (unelevated) app tries to read it.
/// </summary>
public partial class ProcessPickerDialog : Window
{
    private List<ProcessEntry> _allEntries = new();

    /// <summary>The resolved executable name, e.g. "ForzaHorizon6.exe". Null if cancelled.</summary>
    public string? SelectedProcessName { get; private set; }

    /// <summary>Full path to the executable, if readable. Null for protected/inaccessible processes.</summary>
    public string? SelectedExePath { get; private set; }

    public ProcessPickerDialog()
    {
        InitializeComponent();
        DwmInterop.ApplyModernChrome(this, BackdropType.None);
        Loaded += (_, _) => LoadProcesses();
    }

    private void LoadProcesses()
    {
        var entries = new List<ProcessEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (string.IsNullOrWhiteSpace(process.ProcessName))
                    continue;

                if (!seen.Add(process.ProcessName))
                    continue;

                string title = "";
                try { title = process.MainWindowTitle; } catch { /* ignore */ }

                // Best-effort path for the icon; throws for protected/elevated processes — stays null then.
                string? exePath = null;
                try { exePath = process.MainModule?.FileName; } catch { /* access denied: leave null */ }

                entries.Add(new ProcessEntry($"{process.ProcessName}.exe", title, exePath));
            }
            catch
            {
                // Process may have exited mid-enumeration or be inaccessible; skip it.
            }
            finally
            {
                process.Dispose();
            }
        }

        _allEntries = entries.OrderBy(e => e.ExeName, StringComparer.OrdinalIgnoreCase).ToList();
        ProcessList.ItemsSource = _allEntries;
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var filter = FilterBox.Text.Trim();
        ProcessList.ItemsSource = string.IsNullOrEmpty(filter)
            ? _allEntries
            : _allEntries.Where(en =>
                en.ExeName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                en.WindowTitle.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e) => Commit();

    private void ProcessList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Commit();

    private void Commit()
    {
        if (ProcessList.SelectedItem is ProcessEntry entry)
        {
            SelectedProcessName = entry.ExeName;
            SelectedExePath = entry.ExePath;
            DialogResult = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
