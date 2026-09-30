using System.Windows;
using System.Windows.Controls;

namespace SpotifyDeck;

public partial class ShortcutWindow : Window
{
    public string SelectedShortcut { get; private set; }

    public ShortcutWindow(string current)
    {
        InitializeComponent();
        SelectedShortcut = current;

        foreach (ComboBoxItem item in ShortcutCombo.Items)
            if ((string?)item.Tag == current)
                ShortcutCombo.SelectedItem = item;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ShortcutCombo.SelectedItem is ComboBoxItem item && item.Tag is string value)
            SelectedShortcut = value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}