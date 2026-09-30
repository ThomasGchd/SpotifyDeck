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

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;

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
    private int _searchVersion;
    private bool _isShuttingDown;
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
                var usable = _spotifyAuth.HasSession || connected;
                UpdateConnectionUi(usable);
                if (usable)
                    await RefreshSpotifyUiAsync();
            });
        };

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;

        _settings = _settingsService.Load();
        ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
        RegisterGlobalHotkey(_settings.Shortcut);
        CreateTrayIcon();
    }

    public async Task InitializeAsync()
    {
        // Official Spotify OAuth/Web API is the production path.
        // Keep the local bridge alive as the current working fallback while OAuth
        // is not configured in release builds.
        try
        {
            if (!_spotifyAuth.HasSession && _installer.IsInstalled())
                await _installer.EnsureInstalledAsync();

            await _bridge.StartAsync();
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("bridge-startup", ex);
        }

        UpdateConnectionUi(_spotifyAuth.HasSession || _bridge.IsConnected);
        if (_spotifyAuth.HasSession || _bridge.IsConnected)
            await RefreshSpotifyUiAsync();
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

    public async Task ShowOverlayAsync()
    {
        PositionOverlay();
        Show();
        Activate();
        PromoteOverlay();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        if (!_spotifyAuth.HasSession && !_bridge.IsConnected)
        {
            StatusText.Text = "Spotify n'est pas encore connecté.";
            UpdateConnectionUi(false);
            return;
        }

        await _spotify.EnsureRunningHiddenAsync();
        await RefreshSpotifyUiAsync();
    }

    private void PromoteOverlay()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        SetWindowPos(
            handle,
            HwndTopmost,
            0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpShowWindow);
        SetForegroundWindow(handle);
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
                await RefreshSpotifyUiAsync();
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

        var bridgeReady = await _installer.EnsureInstalledAsync();
        if (!bridgeReady)
        {
            StatusText.Text = "Connexion Spotify indisponible sur cette build.";
            ConnectButton.IsEnabled = true;
            return;
        }

        await _spotify.EnsureRunningHiddenAsync();

        for (var i = 0; i < 24 && !_bridge.IsConnected; i++)
            await Task.Delay(250);

        UpdateConnectionUi(_bridge.IsConnected);
        if (_bridge.IsConnected)
            await RefreshSpotifyUiAsync();
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

            _isShuttingDown = true;
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
        _searchVersion++;

        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            Results.Clear();
            StatusText.Text = "Tape un titre, un artiste ou une playlist.";
            return;
        }

        _searchTimer.Start();
    }

    private async void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();

        if ((!_spotifyAuth.HasSession && !_bridge.IsConnected) || string.IsNullOrWhiteSpace(SearchBox.Text))
            return;

        var version = _searchVersion;
        var query = SearchBox.Text.Trim();
        StatusText.Text = "Recherche…";

        try
        {
            var results = _spotifyAuth.HasSession
                ? await _spotifyApi.SearchAsync(query)
                : await _bridge.SearchAsync(query);

            if (version != _searchVersion)
                return;

            Results.Clear();
            foreach (var item in results)
                Results.Add(item);

            StatusText.Text = results.Count == 0
                ? "Aucun résultat."
                : $"{results.Count} résultat(s) · Entrée pour lire.";
        }
        catch (Exception ex)
        {
            if (version != _searchVersion)
                return;

            Results.Clear();
            StatusText.Text = "La recherche Spotify a échoué.";
            await AppLog.WriteAsync("spotify-search", ex);
        }
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

    private async void QuickList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not System.Windows.Controls.ListBox list)
            return;

        if (list.SelectedItem is SpotifyItem item)
        {
            await PlayAsync(item);
            e.Handled = true;
        }
    }

    private async Task PlayAsync(SpotifyItem item)
    {
        StatusText.Text = $"Lecture de {item.Name}…";

        try
        {
            await _spotify.EnsureRunningHiddenAsync();
            var ok = _spotifyAuth.HasSession
                ? await _spotifyApi.PlayAsync(item)
                : await _bridge.PlayAsync(item);

            if (ok)
            {
                Hide();
                SearchBox.Clear();
                Results.Clear();
                return;
            }

            StatusText.Text = "Aucun lecteur Spotify disponible pour lancer ce morceau.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Spotify n'a pas pu lancer cette lecture.";
            await AppLog.WriteAsync("spotify-play", ex);
        }
    }

    private Task RefreshSpotifyUiAsync() =>
        Task.WhenAll(LoadQuickAccessAsync(), RefreshPlaybackAsync());

    private async Task RefreshPlaybackAsync()
    {
        try
        {
            var state = _spotifyAuth.HasSession
                ? await _spotifyApi.GetPlaybackStateAsync()
                : await _bridge.GetPlaybackStateAsync();

            NowPlayingText.Text = state is null
                ? "Aucun morceau en lecture"
                : $"{(state.IsPlaying ? "▶" : "⏸")} {state.Name} · {state.Artist}";
        }
        catch (Exception ex)
        {
            NowPlayingText.Text = "Lecture Spotify indisponible";
            await AppLog.WriteAsync("spotify-state", ex);
        }
    }

    private async Task LoadQuickAccessAsync()
    {
        if (!_spotifyAuth.HasSession && !_bridge.IsConnected)
            return;

        try
        {
            await Task.WhenAll(LoadRecentAsync(), LoadPlaylistsAsync());
        }
        catch (Exception ex)
        {
            await AppLog.WriteAsync("spotify-quick-access", ex);
            StatusText.Text = "Spotify connecté · accès rapide temporairement indisponible.";
        }
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

    private async void PreviousButton_Click(object sender, RoutedEventArgs e) =>
        await RunTransportAsync(
            () => _spotifyApi.PreviousAsync(),
            () => _bridge.PreviousAsync(),
            "Morceau précédent.");

    private async void ToggleButton_Click(object sender, RoutedEventArgs e) =>
        await RunTransportAsync(
            () => _spotifyApi.TogglePlaybackAsync(),
            () => _bridge.TogglePlaybackAsync(),
            "Lecture / pause.");

    private async void NextButton_Click(object sender, RoutedEventArgs e) =>
        await RunTransportAsync(
            () => _spotifyApi.NextAsync(),
            () => _bridge.NextAsync(),
            "Morceau suivant.");

    private async Task RunTransportAsync(
        Func<Task<bool>> officialAction,
        Func<Task<bool>> bridgeAction,
        string successMessage)
    {
        if (!_spotifyAuth.HasSession && !_bridge.IsConnected)
            return;

        try
        {
            await _spotify.EnsureRunningHiddenAsync();
            var ok = _spotifyAuth.HasSession
                ? await officialAction()
                : await bridgeAction();

            StatusText.Text = ok
                ? successMessage
                : "Commande Spotify indisponible.";

            if (ok)
                await RefreshPlaybackAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Commande Spotify impossible.";
            await AppLog.WriteAsync("spotify-transport", ex);
        }
    }

    private void UpdateConnectionUi(bool connected)
    {
        ConnectButton.Content = connected ? "Spotify connecté" : "Connecter Spotify";
        ConnectButton.IsEnabled = !connected;
        SearchBox.IsEnabled = connected;
        PreviousButton.IsEnabled = connected;
        ToggleButton.IsEnabled = connected;
        NextButton.IsEnabled = connected;

        if (!connected)
            NowPlayingText.Text = "Aucun morceau en lecture";

        StatusText.Text = connected
            ? $"Spotify connecté · {_hotkeyLabel} pour afficher/masquer."
            : $"Connecte Spotify une fois · raccourci : {_hotkeyLabel}.";
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isShuttingDown)
            return;

        // SpotifyDeck lives in the tray. Alt+F4 should hide the overlay rather
        // than destroy the only window and leave an unusable tray process.
        e.Cancel = true;
        Hide();
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

    public void PrepareForShutdown() => _isShuttingDown = true;

    private async Task ShutdownAsync()
    {
        _isShuttingDown = true;

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
