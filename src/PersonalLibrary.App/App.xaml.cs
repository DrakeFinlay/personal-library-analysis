using System.IO;
using System.Windows;
using System.Windows.Media;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

public partial class App : Application
{
    private LibraryStore? store;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var isolated = Environment.GetEnvironmentVariable("PERSONAL_LIBRARY_DATA_DIR");
            var root = string.IsNullOrWhiteSpace(isolated) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalLibrary") : isolated;
            store = new LibraryStore(root);
            if (e.Args.Contains("--demo"))
            {
                if (string.IsNullOrWhiteSpace(isolated)) throw new InvalidOperationException("Demo mode requires an isolated PERSONAL_LIBRARY_DATA_DIR.");
                if (store.Books().Count == 0) SyntheticLibrary.Seed(store);
            }
            ApplyTheme(store.Setting("theme", "light") == "dark");
            MainWindow = new MainWindow(store, !string.IsNullOrWhiteSpace(isolated) || store.Setting("synthetic", "false") == "true");
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Personal Library could not open its profile. Another instance may already be using it.\n\n" + ex.Message, "Personal Library", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public static void ApplyTheme(bool dark)
    {
        var palette = dark ? new[] { "#202522", "#2B322E", "#F1EEE5", "#B7BCAF", "#505A51", "#7BC9B8" }
            : new[] { "#F8F6F0", "#FFFFFF", "#302B25", "#726B60", "#DDD7CA", "#176B62" };
        string[] keys = ["BackgroundBrush", "SurfaceBrush", "TextBrush", "MutedBrush", "LineBrush", "AccentBrush"];
        for (var i = 0; i < keys.Length; i++) Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
    }
    protected override void OnExit(ExitEventArgs e) { store?.Dispose(); base.OnExit(e); }
}
