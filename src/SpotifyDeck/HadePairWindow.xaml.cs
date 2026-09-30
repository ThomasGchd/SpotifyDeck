using System.Windows;
using SpotifyDeck.Services;

namespace SpotifyDeck;

public partial class HadePairWindow : Window
{
    private readonly HadeService _hade;

    public HadePairWindow(HadeService hade)
    {
        InitializeComponent();
        _hade = hade;
    }

    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        PairButton.IsEnabled = false;
        StatusText.Text = "Connexion à Hade…";

        var ok = await _hade.PairAsync(BaseUrlBox.Text, CodeBox.Text);
        if (ok)
        {
            DialogResult = true;
            Close();
            return;
        }

        StatusText.Text = "Connexion impossible. Vérifie le code et que Hade est accessible.";
        PairButton.IsEnabled = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
