using System.Windows;
using System.Windows.Controls;
using PersonalLibrary.Infrastructure;

namespace PersonalLibrary.App;

internal sealed class CatalogInstallWindow : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private bool finished;

    public long InstalledCount { get; private set; }

    public CatalogInstallWindow(OfflineCatalog catalog, string authors, string editions)
    {
        Title = "Install offline catalog";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 490;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(Ui.Heading("Installing offline catalog", 21));
        var status = Ui.Text("Reading author names…", 14, "MutedBrush");
        status.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(status);
        var cancel = Ui.Button("Cancel", () => { });
        cancel.Click += (_, _) => RequestCancel();
        panel.Children.Add(cancel);
        Content = panel;

        Loaded += async (_, _) =>
        {
            var progress = new Progress<string>(value => { if (!cancellation.IsCancellationRequested) status.Text = value; });
            try
            {
                InstalledCount = await Task.Run(() => catalog.Install(authors, editions, progress, cancellation.Token));
            }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested)
                    MessageBox.Show(this, ex.Message, "Offline catalog installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                finished = true;
                DialogResult = InstalledCount > 0;
            }
        };
        Closed += (_, _) => cancellation.Dispose();

        void RequestCancel()
        {
            cancellation.Cancel();
            status.Text = "Cancelling installation…";
            cancel.IsEnabled = false;
        }

        Closing += (_, args) =>
        {
            if (finished) return;
            args.Cancel = true;
            RequestCancel();
        };
    }
}
