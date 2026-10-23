using System.IO;
using System.Windows;
using System.Windows.Input;
using QSLauncher.App.Services;
using QSLauncher.Core.Models;

namespace QSLauncher.App.Views;

public partial class ModpackFallbackWindow : Window
{
    public ModpackFallbackWindow(string archivePath)
    {
        InitializeComponent();
        Versions =
        [
            "26.3", "1.21.10", "1.21.8", "1.21.4", "1.21.1",
            "1.20.6", "1.20.1", "1.19.2", "1.18.2", "1.16.5", "1.12.2", "1.7.10"
        ];
        Loaders =
        [
            new LoaderOption(LoaderKind.Vanilla, "原版"),
            new LoaderOption(LoaderKind.Fabric, "Fabric"),
            new LoaderOption(LoaderKind.Forge, "Forge"),
            new LoaderOption(LoaderKind.NeoForge, "NeoForge"),
            new LoaderOption(LoaderKind.Quilt, "Quilt")
        ];
        DataContext = this;
        PackLabel.Text = Path.GetFileName(archivePath);
    }

    public IReadOnlyList<string> Versions { get; }

    public IReadOnlyList<LoaderOption> Loaders { get; }

    public string MinecraftVersion => VersionBox.Text.Trim();

    public LoaderKind Loader => (LoaderBox.SelectedItem as LoaderOption)?.Kind ?? LoaderKind.Vanilla;

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MinecraftVersion))
        {
            AppDialog.Alert("缺少版本", "请填写或选择 Minecraft 版本。");
            return;
        }

        DialogResult = true;
    }

    public sealed record LoaderOption(LoaderKind Kind, string Name);
}
