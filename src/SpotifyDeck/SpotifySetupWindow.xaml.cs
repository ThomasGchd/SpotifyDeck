using System.Diagnostics;
using System.Windows;

namespace SpotifyDeck;

public partial class SpotifySetupWindow : Window
{
    public string ClientId { get; private set; }

    public SpotifySetupWindow(string currentClientId)
    {
        InitializeComponent();
        ClientId = currentClientId;
        ClientIdBox.Text = currentClientId;
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://developer.spotify.com/dashboard")
        {
            UseShellExecute = true
        });
    }

    private void CopyRedirect_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(RedirectBox.Text);
        HintText.Text = "URI copiée.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var value = ClientIdBox.Text.Trim();
        if (value.Length < 10)
        {
            HintText.Text = "Colle le Client ID affiché dans les paramètres de ton application Spotify.";
            return;
        }

        ClientId = value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
