using System.Windows;
using Microsoft.Win32;
using EsoAddons.Models;
using EsoAddons.ViewModels;

namespace EsoAddons;

/// <summary>Pro Tools dialog: profiles/loadouts, backups, multi-PC sync, and auto-update-on-launch.
/// Bound to the live <see cref="MainViewModel"/> so changes reflect in the main window immediately.</summary>
public partial class ProToolsWindow : Window
{
    private readonly MainViewModel _vm;

    public ProToolsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _vm.RefreshSnapshots();
    }

    private static SnapshotEntry? EntryOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as SnapshotEntry;

    private async void ApplySnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } entry) return;
        if (!Confirm($"Apply “{entry.Name}” to this PC?")) return;
        await _vm.ApplySnapshotAsync(entry, RestoreConfigsCheck.IsChecked == true, RemoveExtrasCheck.IsChecked == true);
    }

    private void DeleteSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } entry) return;
        if (!Confirm($"Delete “{entry.Name}”? This can't be undone.")) return;
        _vm.DeleteSnapshot(entry);
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        await _vm.SaveProfileAsync(ProfileNameBox.Text);
        ProfileNameBox.Clear();
    }

    private async void BackupNow_Click(object sender, RoutedEventArgs e) => await _vm.BackupNowAsync();

    private void ChooseSyncFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a sync folder (inside your cloud drive)" };
        if (_vm.HasSyncFolder && System.IO.Directory.Exists(_vm.SyncFolder)) dialog.InitialDirectory = _vm.SyncFolder;
        if (dialog.ShowDialog(this) == true) _vm.SetSyncFolder(dialog.FolderName);
    }

    private async void SyncPush_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Push this PC's addons + settings to the sync folder? This overwrites the synced copy.")) return;
        await _vm.SyncPushAsync();
    }

    private async void SyncPull_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Pull the synced addons + settings onto this PC?")) return;
        await _vm.SyncPullAsync(RemoveExtrasCheck.IsChecked == true);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, "Shoyru Addon Suite — Pro Tools",
            MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
}
