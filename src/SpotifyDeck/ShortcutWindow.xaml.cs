using System.Windows;
using System.Windows.Input;

namespace SpotifyDeck;

public partial class ShortcutWindow : Window
{
    public string SelectedShortcut { get; private set; }

    public ShortcutWindow(string current)
    {
        InitializeComponent();
        SelectedShortcut = current;
        CaptureBox.Text = Format(current);

        Loaded += (_, _) =>
        {
            CaptureBox.Focus();
            Keyboard.Focus(CaptureBox);
        };
    }

    private void CaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            HintText.Text = "Ajoute au moins Ctrl, Alt, Shift ou la touche Windows.";
            e.Handled = true;
            return;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key == Key.Space ? "Space" : key.ToString());

        SelectedShortcut = string.Join("+", parts);
        CaptureBox.Text = Format(SelectedShortcut);
        HintText.Text = "Prêt à être testé.";
        SaveButton.IsEnabled = true;
        e.Handled = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string Format(string shortcut) =>
        shortcut.Replace("+", " + ");
}