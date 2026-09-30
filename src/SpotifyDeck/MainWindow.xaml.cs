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
    private const int FallbackHotkeyId = 0x5345;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly SpotifyAuthService _spotifyAuth = new();
    private readonly SpotifyApiService _spotifyApi;
    private readonly SpicetifyBridgeService _bridge = new();
    private readonly SpotifyProcessService _spotify = new();
    private readonly BridgeInstallerService _installer = new();
    private readonly UpdateService _updates = new();
    private readonly AutoStartService _autoStart = new();
    private readonly AppSettingsService _settingsService = new();
    private SpotifyDeckSettings _settings = new();
    private readonly DispatcherTimer _searchTimer;

    private int _registeredHotkeyId;
    private string _hotkeyLabel = "Ctrl + Alt + M";
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public ObservableCollection<SpotifyItem> Results { get; } = [];
    public ObservableCollection<SpotifyItem> Recent { get; } = [];
    public ObservableCollection<SpotifyItem> Playlists { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        _spotifyApi = new SpotifyApiService(_spotifyAuth);

        ResultsList.ItemsSource = Results;
        RecentList.ItemsSource = Recent;
        PlaylistsList.ItemsSource = Playlists;

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
                    await LoadQuickAccessAsync();
            });
        };

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        _settings = _settingsService.Load();
        ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
        RegisterGlobalHotkey(_settings.Shortcut);
        CreateTrayIcon();
    }

    public async Task InitializeAsync()
    {
        // Official Spotify OAuth/Web API is the production path.
        // Keep the local bridge alive only as a development fallback.
        try
        {
            await _bridge.StartAsync();
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("bridge-startup", ex);
        }

        UpdateConnectionUi(_spotifyAuth.HasSession || _bridge.IsConnected);
        if (_spotifyAuth.HasSession)
            await LoadQuickAccessAsync();
    }

    private bool RegisterGlobalHotkey(string shortcut)
    {
        if (_registeredHotkeyId != 0)
        {
            UnregisterHotKey(IntPtr.Zero, _registeredHotkeyId);
            _registeredHotkeyId = 0;
        }

        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = ModNoRepeat;
        Key key = Key.M;

        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= ModControl;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModAlt;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModShift;
            else if (part.Equals("Space", StringComparison.OrdinalIgnoreCase)) key = Key.Space;
            else if (Enum.TryParse<Key>(part, true, out var parsed)) key = parsed;
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(IntPtr.Zero, HotkeyId, modifiers, vk))
        {
            var error = Marshal.GetLastWin32Error();
            _hotkeyLabel = "raccourci indisponible";
            StatusText.Text = $"{shortcut.Replace("+", " + ")} est déjà utilisé · choisis un autre raccourci.";
            ShortcutText.Text = "Raccourci indisponible · clique sur Raccourci";
            _ = AppLog.WriteAsync("hotkey", $"Échec {shortcut} (Win32 {error}).");
            return false;
        }

        _registeredHotkeyId = HotkeyId;
        _hotkeyLabel = shortcut.Replace("+", " + ");
        ShortcutText.Text = $"{_hotkeyLabel} · ta musique sans quitter le jeu";
        return true;
    }

    private void ShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ShortcutWindow(_settings.Shortcut) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var previous = _settings.Shortcut;
        var selected = dialog.SelectedShortcut;

        if (!RegisterGlobalHotkey(selected))
        {
            RegisterGlobalHotkey(previous);
            return;
        }

        _settings = _settings with { Shortcut = selected };
        _settingsService.Save(_settings);
        StatusText.Text = $"{_hotkeyLabel} enregistré.";
        if (_trayIcon is not null)
            _trayIcon.Text = $"SpotifyDeck · {_hotkeyLabel}";
    }

    private void OnThreadFilterMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != WmHotkey)
            return;

        var id = msg.wParam.ToInt32();
        if (id != HotkeyId && id != FallbackHotkeyId)
            return;

        handled = true;

        if (IsVisible)
            Hide();
        else
            _ = ShowOverlayAsync();
    }

    private async Task ShowOverlayAsync()
    {
        PositionOverlay();
        Show();
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        if (!_spotifyAuth.HasSession && !_bridge.IsConnected)
        {
            StatusText.Text = "Spotify n'est pas encore connecté.";
            UpdateConnectionUi(false);
            return;
        }

        await _spotify.EnsureRunningHiddenAsync();
        await LoadQuickAccessAsync();
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

        if (_spotifyAuth.IsConfigured)
        {
            StatusText.Text = "Ouverture de Spotify dans ton navigateur…";
            var result = await _spotifyAuth.ConnectAsync();
            StatusText.Text = result.Message;

            if (result.Success)
            {
                UpdateConnectionUi(true);
                await _spotify.EnsureRunningHiddenAsync();
                await LoadQuickAccessAsync();
            }
            else
            {
                ConnectButton.IsEnabled = true;
            }

            return;
        }

        // Temporary development fallback until the SpotifyDeck OAuth app id
        // is injected in release builds.
        StatusText.Text = "Connexion locale Spotify…";

        if (!_installer.IsInstalled())
        {
            var installed = await _installer.EnsureInstalledAsync();
            if (!installed)
            {
                StatusText.Text = "Connexion Spotify indisponible sur cette build.";
                ConnectButton.IsEnabled = true;
                return;
            }
        }

        await _spotify.EnsureRunningHiddenAsync();

        for (var i = 0; i < 24 && !_bridge.IsConnected; i++)
            await Task.Delay(250);

        UpdateConnectionUi(_bridge.IsConnected);
        if (_bridge.IsConnected)
            await LoadQuickAccessAsync();
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

            var answer = System.Windows.MessageBox.Show(
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

        if ((!_spotifyAuth.HasSession && !_bridge.IsConnected) || string.IsNullOrWhiteSpace(SearchBox.Text))
            return;

        StatusText.Text = "Recherche…";
        var results = _spotifyAuth.HasSession
            ? await _spotifyApi.SearchAsync(SearchBox.Text.Trim())
            : await _bridge.SearchAsync(SearchBox.Text.Trim());

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
            await PlayAsync(item);
            e.Handled = true;
        }
    }

    private async void RecentList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is SpotifyItem item)
            await PlayAsync(item);
    }

    private async void PlaylistsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistsList.SelectedItem is SpotifyItem item)
            await PlayAsync(item);
    }

    private async Task PlayAsync(SpotifyItem item)
    {
        StatusText.Text = $"Lecture de {item.Name}…";

        await _spotify.EnsureRunningHiddenAsync();
        var ok = _spotifyAuth.HasSession
            ? await _spotifyApi.PlayAsync(item)
            : await _bridge.PlayAsync(item);
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

    private async Task LoadQuickAccessAsync()
    {
        if (!_spotifyAuth.HasSession && !_bridge.IsConnected)
            return;

        await Task.WhenAll(LoadRecentAsync(), LoadPlaylistsAsync());
    }

    private async Task LoadRecentAsync()
    {
        var recent = _spotifyAuth.HasSession
            ? await _spotifyApi.GetRecentAsync()
            : await _bridge.GetRecentAsync();

        Recent.Clear();
        foreach (var item in recent.Take(4))
            Recent.Add(item);
    }

    private async Task LoadPlaylistsAsync()
    {
        var playlists = _spotifyAuth.HasSession
            ? await _spotifyApi.GetPlaylistsAsync()
            : await _bridge.GetPlaylistsAsync();

        Playlists.Clear();
        foreach (var playlist in playlists.Take(5))
            Playlists.Add(playlist);
    }

    private void UpdateConnectionUi(bool connected)
    {
        ConnectButton.Content = connected ? "Spotify connecté" : "Connecter Spotify";
        ConnectButton.IsEnabled = !connected;
        SearchBox.IsEnabled = connected;

        StatusText.Text = connected
            ? $"Spotify connecté · {_hotkeyLabel} pour afficher/masquer."
            : $"Connecte Spotify une fois · raccourci : {_hotkeyLabel}.";
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
            Text = $"SpotifyDeck · {_hotkeyLabel}",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        var show = menu.Items.Add("Afficher SpotifyDeck");
        var update = menu.Items.Add("Rechercher une mise à jour");
        var autoStart = new System.Windows.Forms.ToolStripMenuItem("Démarrer avec Windows")
        {
            Checked = _autoStart.IsEnabled,
            CheckOnClick = true
        };
        menu.Items.Add(autoStart);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var exit = menu.Items.Add("Quitter");

        show.Click += (_, _) => Dispatcher.Invoke(() => _ = ShowOverlayAsync());
        update.Click += (_, _) => Dispatcher.Invoke(() => UpdateButton_Click(UpdateButton, new RoutedEventArgs()));
        var syncingAutoStart = false;
        autoStart.CheckedChanged += (_, _) =>
        {
            if (syncingAutoStart)
                return;

            var wanted = autoStart.Checked;
            if (_autoStart.SetEnabled(wanted))
                return;

            syncingAutoStart = true;
            autoStart.Checked = !wanted;
            syncingAutoStart = false;
            Dispatcher.Invoke(() => StatusText.Text = "Impossible de modifier le démarrage automatique.");
        };
        exit.Click += (_, _) => Dispatcher.Invoke(async () => await ShutdownAsync());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => _ = ShowOverlayAsync());
    }

    private async Task ShutdownAsync()
    {
        if (_registeredHotkeyId != 0)
            UnregisterHotKey(IntPtr.Zero, _registeredHotkeyId);

        ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        await _bridge.DisposeAsync();
        System.Windows.Application.Current.Shutdown();
    }
}
