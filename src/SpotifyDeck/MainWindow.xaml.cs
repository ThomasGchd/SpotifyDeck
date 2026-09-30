using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using SpotifyDeck.Models;
using SpotifyDeck.Services;

namespace SpotifyDeck;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x5344;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly SpicetifyBridgeService _bridge = new();
    private readonly SpotifyProcessService _spotify = new();
    private readonly BridgeInstallerService _installer = new();
    private readonly UpdateService _updates = new();
    private readonly HadeService _hade = new();
    private readonly DispatcherTimer _searchTimer;

    private HwndSource? _source;
    private IntPtr _handle;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public ObservableCollection<SpotifyItem> Results { get; } = [];
    public ObservableCollection<SpotifyItem> Playlists { get; } = [];

    public MainWindow()
    {
        InitializeComponent();

        ResultsList.ItemsSource = Results;
        PlaylistsList.ItemsSource = Playlists;
        HadeButton.Content = _hade.IsPaired ? "Hade connecté" : "Connecter Hade";

        _searchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(260)
        };
        _searchTimer.Tick += SearchTimer_Tick;

        _bridge.ConnectionChanged += connected =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                UpdateConnectionUi(connected);
                if (connected)
                    await LoadPlaylistsAsync();
            });
        };

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Deactivated += (_, _) =>
        {
            if (IsVisible)
                Hide();
        };

        CreateTrayIcon();

        // Create the native HWND without ever flashing the overlay on screen.
        _handle = new WindowInteropHelper(this).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        var vkM = (uint)KeyInterop.VirtualKeyFromKey(Key.M);
        if (!RegisterHotKey(_handle, HotkeyId, ModControl | ModAlt, vkM))
            StatusText.Text = "Ctrl + Alt + M est déjà utilisé par une autre application.";
    }

    public async Task InitializeAsync()
    {
        await _bridge.StartAsync();
        UpdateConnectionUi(_bridge.IsConnected);
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;

            if (IsVisible)
                Hide();
            else
                _ = ShowOverlayAsync();
        }

        return IntPtr.Zero;
    }

    private async Task ShowOverlayAsync()
    {
        PositionOverlay();
        Show();
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        if (!_bridge.IsConnected)
        {
            StatusText.Text = "Connexion à Spotify…";
            await _spotify.EnsureRunningHiddenAsync();

            for (var i = 0; i < 12 && !_bridge.IsConnected; i++)
                await Task.Delay(250);

            UpdateConnectionUi(_bridge.IsConnected);
        }
        else
        {
            await LoadPlaylistsAsync();
        }
    }

    private void PositionOverlay()
    {
        var point = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(point);
        var area = screen.WorkingArea;

        Left = area.Left + (area.Width - Width) / 2.0;
        Top = area.Top + Math.Max(36, (area.Height - Height) / 3.0);
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        StatusText.Text = "Préparation de SpotifyDeck…";

        if (!_installer.IsInstalled())
        {
            StatusText.Text = "Installation du bridge SpotifyDeck…";
            var installed = await _installer.EnsureInstalledAsync();

            if (!installed)
            {
                StatusText.Text = "Spicetify est requis pour connecter Spotify.";
                ConnectButton.IsEnabled = true;
                return;
            }
        }

        await _spotify.EnsureRunningHiddenAsync();

        StatusText.Text = "Connexion à Spotify…";
        for (var i = 0; i < 24 && !_bridge.IsConnected; i++)
            await Task.Delay(250);

        UpdateConnectionUi(_bridge.IsConnected);
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        StatusText.Text = "Recherche d'une mise à jour…";

        try
        {
            var update = await _updates.CheckAsync();
            if (update is null)
            {
                StatusText.Text = "SpotifyDeck est à jour.";
                return;
            }

            var answer = MessageBox.Show(
                $"SpotifyDeck {update.Version} est disponible. Installer maintenant ?",
                "Mise à jour SpotifyDeck",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text = $"Mise à jour {update.Version} disponible.";
                return;
            }

            StatusText.Text = $"Téléchargement de SpotifyDeck {update.Version}…";
            var started = await _updates.DownloadAndApplyAsync(update);

            if (!started)
            {
                StatusText.Text = "La mise à jour n'a pas pu être préparée.";
                return;
            }

            System.Windows.Application.Current.Shutdown();
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private void HadeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HadePairWindow(_hade)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            HadeButton.Content = "Hade connecté";
            StatusText.Text = "Hade connecté · Shift+Entrée ajoutera le morceau à la file Discord.";
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _searchTimer.Stop();

        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            Results.Clear();
            return;
        }

        _searchTimer.Start();
    }

    private async void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();

        if (!_bridge.IsConnected || string.IsNullOrWhiteSpace(SearchBox.Text))
            return;

        StatusText.Text = "Recherche…";
        var results = await _bridge.SearchAsync(SearchBox.Text.Trim());

        Results.Clear();
        foreach (var item in results)
            Results.Add(item);

        StatusText.Text = results.Count == 0
            ? "Aucun résultat."
            : $"{results.Count} résultat(s).";
    }

    private async void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Down && Results.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            ResultsList.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && Results.Count > 0)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                await QueueToHadeAsync(Results[0]);
            else
                await PlayAsync(Results[0]);

            e.Handled = true;
        }
    }

    private async void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is SpotifyItem item)
            await PlayAsync(item);
    }

    private async void ResultsList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ResultsList.SelectedItem is SpotifyItem item)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                await QueueToHadeAsync(item);
            else
                await PlayAsync(item);

            e.Handled = true;
        }
    }

    private async void PlaylistsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistsList.SelectedItem is SpotifyItem item)
            await PlayAsync(item);
    }

    private async Task QueueToHadeAsync(SpotifyItem item)
    {
        if (item.Type != "track")
        {
            StatusText.Text = "La file Discord accepte un morceau, pas une playlist.";
            return;
        }

        if (!_hade.IsPaired)
        {
            StatusText.Text = "Connecte Hade d'abord.";
            HadeButton_Click(HadeButton, new RoutedEventArgs());
            return;
        }

        StatusText.Text = $"Ajout de {item.Name} à la file Discord…";
        var result = await _hade.QueueAsync(item.Uri);
        StatusText.Text = result.Message;

        if (result.Success)
        {
            await Task.Delay(450);
            Hide();
            SearchBox.Clear();
            Results.Clear();
        }
    }

    private async Task PlayAsync(SpotifyItem item)
    {
        StatusText.Text = $"Lecture de {item.Name}…";

        var ok = await _bridge.PlayAsync(item);
        if (ok)
        {
            Hide();
            SearchBox.Clear();
            Results.Clear();
        }
        else
        {
            StatusText.Text = "Spotify n'a pas pu lancer cette lecture.";
        }
    }

    private async Task LoadPlaylistsAsync()
    {
        if (!_bridge.IsConnected)
            return;

        var playlists = await _bridge.GetPlaylistsAsync();

        Playlists.Clear();
        foreach (var playlist in playlists.Take(12))
            Playlists.Add(playlist);
    }

    private void UpdateConnectionUi(bool connected)
    {
        ConnectButton.Content = connected ? "Spotify connecté" : "Connecter Spotify";
        ConnectButton.IsEnabled = !connected;
        SearchBox.IsEnabled = connected;

        StatusText.Text = connected
            ? "Spotify connecté · Ctrl + Alt + M pour afficher/masquer."
            : "Connecte Spotify une fois, puis SpotifyDeck travaillera en arrière-plan.";
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "SpotifyDeck · Ctrl+Alt+M",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        var show = menu.Items.Add("Afficher SpotifyDeck");
        var update = menu.Items.Add("Rechercher une mise à jour");
        var exit = menu.Items.Add("Quitter");

        show.Click += (_, _) => Dispatcher.Invoke(() => _ = ShowOverlayAsync());
        update.Click += (_, _) => Dispatcher.Invoke(() => UpdateButton_Click(UpdateButton, new RoutedEventArgs()));
        exit.Click += (_, _) => Dispatcher.Invoke(async () => await ShutdownAsync());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => _ = ShowOverlayAsync());
    }

    private async Task ShutdownAsync()
    {
        if (_handle != IntPtr.Zero)
            UnregisterHotKey(_handle, HotkeyId);

        _source?.RemoveHook(WndProc);

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        await _bridge.DisposeAsync();
        System.Windows.Application.Current.Shutdown();
    }
}
