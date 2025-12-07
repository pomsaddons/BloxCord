using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using BloxCord.Client.Models;
using BloxCord.Client.Services;
using BloxCord.Client.ViewModels;

namespace BloxCord.Client;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private ChatClient? _chatClient;
    private readonly Dictionary<string, ParticipantViewModel> _participantLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, string> _avatarUrlCache = new();
    private readonly DispatcherTimer _typingTimer;
    private bool _isTypingLocally;

    public MainWindow()
    {
        InitializeComponent();
        
        // Load config
        ConfigService.Load();
        _viewModel.BackendUrl = ConfigService.Current.BackendUrl;
        _viewModel.Username = ConfigService.Current.Username;
        
        // Apply theme
        if (ConfigService.Current.UseGradient)
        {
            try
            {
                var startColor = (Color)ColorConverter.ConvertFromString(ConfigService.Current.GradientStart);
                var endColor = (Color)ColorConverter.ConvertFromString(ConfigService.Current.GradientEnd);
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                gradient.GradientStops.Add(new GradientStop(startColor, 0));
                gradient.GradientStops.Add(new GradientStop(endColor, 1));
                MainGrid.Background = gradient;
            }
            catch { }
        }
        else
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(ConfigService.Current.SolidColor);
                MainGrid.Background = new SolidColorBrush(color);
            }
            catch { }
        }

        DataContext = _viewModel;
        Closed += async (_, _) => await DisposeClientAsync();

        _typingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };

        _typingTimer.Tick += TypingTimer_Tick;

        _viewModel.Messages.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                MessagesScrollViewer.ScrollToBottom();
            }
        };
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsServerBrowserVisible = false;
        // Auto-load session info first
        _viewModel.StatusMessage = "Reading Roblox logs...";
        try
        {
            var session = await RobloxLogParser.TryReadLatestAsync();
            if (session is not null)
            {
                if (!string.IsNullOrWhiteSpace(session.Username))
                    _viewModel.Username = session.Username;

                _viewModel.JobId = session.JobId;
                _viewModel.UserId = session.UserId?.ToString() ?? string.Empty;
                _viewModel.SessionUserId = session.UserId;
                _viewModel.PlaceId = session.PlaceId;
                _viewModel.StatusMessage = "Session info loaded from Roblox logs.";
            }
            else
            {
                _viewModel.StatusMessage = "No active Roblox session detected.";
                MessageBox.Show("Could not find an active Roblox session in the logs. Please ensure you are in a game.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Failed to read logs: {ex.Message}";
            MessageBox.Show($"Failed to read logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.BackendUrl) ||
            string.IsNullOrWhiteSpace(_viewModel.JobId) ||
            string.IsNullOrWhiteSpace(_viewModel.Username))
        {
            _viewModel.StatusMessage = "Backend URL, Job ID, and Username are required.";
            MessageBox.Show("Connection details are missing even after reading logs.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await DisposeClientAsync();
            _viewModel.StatusMessage = "Connecting to chat server...";

            _chatClient = new ChatClient(_viewModel.BackendUrl);
            _chatClient.MessageReceived += HandleMessageReceived;
            _chatClient.ParticipantsChanged += HandleParticipantsChanged;
            _chatClient.HistoryReceived += HandleHistoryReceived;
            _chatClient.TypingIndicatorReceived += HandleTypingIndicator;

            _participantLookup.Clear();
            _viewModel.Participants.Clear();
            _viewModel.ResetMessages();

            var userId = ResolveUserId();

            await _chatClient.ConnectAsync(_viewModel.Username, _viewModel.JobId, userId, _viewModel.PlaceId);

            _viewModel.IsConnected = true;
            _viewModel.StatusMessage = $"Connected to channel {_viewModel.JobId}.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Connect failed: {ex.Message}";
            MessageBox.Show($"Connect failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        await TryNotifyTypingAsync(false);
        await DisposeClientAsync();
        _participantLookup.Clear();
        _viewModel.Participants.Clear();
        _viewModel.ResetMessages();
        _viewModel.IsConnected = false;
        _viewModel.StatusMessage = "Disconnected.";
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.OutgoingMessage))
            return;

        if (_chatClient is null)
        {
            _viewModel.StatusMessage = "Connect before sending messages.";
            return;
        }

        try
        {
            var message = _viewModel.OutgoingMessage.Trim();

            if (message.Length == 0)
                return;

            await _chatClient.SendAsync(message);
            _viewModel.OutgoingMessage = string.Empty;
            await TryNotifyTypingAsync(false);
            _typingTimer.Stop();
            _isTypingLocally = false;
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Send failed: {ex.Message}";
        }
    }

    private async void HandleMessageReceived(object? sender, ChatMessageDto dto)
    {
        var resolution = await ResolveAvatarAsync(dto.UserId, dto.Username, dto.AvatarUrl);

        await Dispatcher.InvokeAsync(() =>
        {
            _viewModel.Messages.Add(new ClientChatMessage
            {
                Content = dto.Content,
                JobId = dto.JobId,
                Username = dto.Username,
                Timestamp = dto.Timestamp.LocalDateTime,
                UserId = resolution.UserId,
                AvatarUrl = resolution.AvatarUrl
            });
        }, DispatcherPriority.Background);
    }

    private async void HandleParticipantsChanged(object? sender, List<ChannelParticipantDto> participants)
    {
        try 
        {
            var ordered = participants
                .OrderBy(p => p.Username, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await Dispatcher.InvokeAsync(() =>
            {
                var incomingSet = new HashSet<string>(ordered.Select(p => p.Username), StringComparer.OrdinalIgnoreCase);
                
                // Detect Left
                var toRemove = _participantLookup.Keys
                    .Where(existing => !incomingSet.Contains(existing))
                    .ToList();

                foreach (var username in toRemove)
                {
                    if (_participantLookup.TryGetValue(username, out var existingVm))
                    {
                        _participantLookup.Remove(username);
                        _viewModel.Participants.Remove(existingVm);
                        
                        // Add System Message
                        _viewModel.Messages.Add(new ClientChatMessage
                        {
                            Username = "System",
                            Content = $"{username} left the chat",
                            Timestamp = DateTime.Now,
                            JobId = _viewModel.JobId,
                            IsSystemMessage = true
                        });
                    }
                }

                // Detect Joined
                foreach (var dto in ordered)
                {
                    if (!_participantLookup.TryGetValue(dto.Username, out var vm))
                    {
                        vm = new ParticipantViewModel
                        {
                            Username = dto.Username,
                            UserId = dto.UserId,
                            AvatarUrl = dto.AvatarUrl ?? string.Empty
                        };

                        _participantLookup[dto.Username] = vm;
                        InsertParticipantSorted(vm);
                        _ = EnsureAvatarForParticipantAsync(vm);

                        // Add System Message (only if we are already connected to avoid spam on initial load)
                        if (_viewModel.IsConnected) 
                        {
                            _viewModel.Messages.Add(new ClientChatMessage
                            {
                                Username = "System",
                                Content = $"{dto.Username} joined the chat",
                                Timestamp = DateTime.Now,
                                JobId = _viewModel.JobId,
                                IsSystemMessage = true,
                                AvatarUrl = dto.AvatarUrl ?? string.Empty
                            });
                        }
                    }
                    else
                    {
                        // Update existing participant details if needed
                        if (vm.UserId != dto.UserId || vm.AvatarUrl != dto.AvatarUrl)
                        {
                            vm.UserId = dto.UserId;
                            vm.AvatarUrl = dto.AvatarUrl ?? string.Empty;
                            _ = EnsureAvatarForParticipantAsync(vm);
                        }
                    }
                }
                
                _viewModel.ParticipantCount = _viewModel.Participants.Count;
                _viewModel.StatusMessage = $"Received {participants.Count} participants. Count: {_viewModel.ParticipantCount}";

            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
             Dispatcher.Invoke(() => 
             {
                _viewModel.StatusMessage = $"Error updating participants: {ex.Message}";
                MessageBox.Show($"Error updating participants: {ex.Message}");
             });
        }
    }

    private async void HandleHistoryReceived(object? sender, List<ChatMessageDto> history)
    {
        var ordered = history
            .OrderBy(m => m.Timestamp)
            .ToList();

        var prepared = new List<ClientChatMessage>();

        foreach (var entry in ordered)
        {
            var resolution = await ResolveAvatarAsync(entry.UserId, entry.Username, entry.AvatarUrl);

            prepared.Add(new ClientChatMessage
            {
                Content = entry.Content,
                JobId = entry.JobId,
                Username = entry.Username,
                Timestamp = entry.Timestamp.LocalDateTime,
                UserId = resolution.UserId,
                AvatarUrl = resolution.AvatarUrl
            });
        }

        await Dispatcher.InvokeAsync(() =>
        {
            _viewModel.ResetMessages();

            foreach (var message in prepared)
                _viewModel.Messages.Add(message);

            // Add Safety Warning
            _viewModel.Messages.Add(new ClientChatMessage
            {
                Username = "System",
                Content = "This chat is unmoderated. Be careful sharing personal information and connecting outside of RoChat.",
                Timestamp = DateTime.Now,
                JobId = _viewModel.JobId,
                IsSystemMessage = true,
                AvatarUrl = string.Empty
            });
        }, DispatcherPriority.Background);
    }

    private void HandleTypingIndicator(object? sender, TypingIndicatorDto payload)
    {
        Dispatcher.Invoke(() =>
        {
            var active = new HashSet<string>(payload.Usernames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _participantLookup)
                kvp.Value.IsTyping = active.Contains(kvp.Key);

            _viewModel.UpdateTypingIndicator(active);
        }, DispatcherPriority.Background);
    }

    private async void MessageInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_chatClient is null || !_viewModel.IsConnected)
            return;

        if (!_isTypingLocally)
        {
            _isTypingLocally = true;
            await TryNotifyTypingAsync(true);
        }

        _typingTimer.Stop();
        _typingTimer.Start();
    }

    private async void TypingTimer_Tick(object? sender, EventArgs e)
    {
        _typingTimer.Stop();

        if (_isTypingLocally)
        {
            _isTypingLocally = false;
            await TryNotifyTypingAsync(false);
        }
    }

    private async Task DisposeClientAsync()
    {
        if (_chatClient is null)
            return;

        try
        {
            await _chatClient.DisposeAsync();
        }
        catch
        {
            // ignore shutdown exceptions
        }
        finally
        {
            _chatClient = null;
        }
    }

    private long? ResolveUserId()
    {
        if (_viewModel.SessionUserId.HasValue)
            return _viewModel.SessionUserId;

        if (long.TryParse(_viewModel.UserId, out var parsed))
            return parsed;

        return null;
    }

    private void InsertParticipantSorted(ParticipantViewModel participant)
    {
        int index = 0;

        while (index < _viewModel.Participants.Count &&
               string.Compare(_viewModel.Participants[index].Username, participant.Username, StringComparison.OrdinalIgnoreCase) < 0)
        {
            index++;
        }

        _viewModel.Participants.Insert(index, participant);
    }

    private async Task EnsureAvatarForParticipantAsync(ParticipantViewModel participant)
    {
        var resolution = await ResolveAvatarAsync(participant.UserId, participant.Username, participant.AvatarUrl);

        if (string.IsNullOrEmpty(resolution.AvatarUrl))
            return;

        await Dispatcher.InvokeAsync(() =>
        {
            participant.UserId ??= resolution.UserId;
            participant.AvatarUrl = resolution.AvatarUrl;
        }, DispatcherPriority.Background);
    }

    private async Task<AvatarResolution> ResolveAvatarAsync(long? userId, string username, string? existingUrl)
    {
        if (!string.IsNullOrEmpty(existingUrl))
            return new AvatarResolution(userId, existingUrl);

        if (userId.HasValue && _avatarUrlCache.TryGetValue(userId.Value, out var cached))
            return new AvatarResolution(userId, cached);

        var resolved = await RobloxAvatarDirectory.TryResolveAsync(userId, username);

        if (resolved?.UserId is long resolvedId && !string.IsNullOrEmpty(resolved.AvatarUrl))
        {
            _avatarUrlCache[resolvedId] = resolved.AvatarUrl;
            return new AvatarResolution(resolvedId, resolved.AvatarUrl);
        }

        return new AvatarResolution(resolved?.UserId ?? userId, resolved?.AvatarUrl ?? string.Empty);
    }

    private async Task TryNotifyTypingAsync(bool isTyping)
    {
        if (_chatClient is null)
            return;

        try
        {
            await _chatClient.NotifyTypingAsync(isTyping);
        }
        catch
        {
            // ignore typing notification issues
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(this);
        settings.Owner = this;
        settings.ShowDialog();
    }

    private void OnGamesListReceived(object? sender, List<GameDto> games)
    {
        Dispatcher.Invoke(() =>
        {
            _viewModel.Games.Clear();
            foreach (var game in games)
                _viewModel.Games.Add(game);
        });
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsServerBrowserVisible = true;
        
        try
        {
            if (_chatClient == null)
            {
                _chatClient = new ChatClient(_viewModel.BackendUrl);
            }

            _chatClient.GamesListReceived -= OnGamesListReceived;
            _chatClient.GamesListReceived += OnGamesListReceived;

            await _chatClient.GetGamesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load games: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsServerBrowserVisible = false;
    }

    private async void JoinServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string jobId)
        {
            // Find the game for this job
            var game = _viewModel.Games.FirstOrDefault(g => g.Servers.Any(s => s.JobId == jobId));
            if (game != null)
            {
                try
                {
                    // Use browser URL to ensure authentication
                    var placeId = game.PlaceId;
                    var targetJobId = game.Servers.FirstOrDefault(s => s.JobId == (string)btn.Tag)?.JobId ?? (string)btn.Tag;

                    // https://www.roblox.com/games/start?placeId=<ID>&gameId=<JOBID>
                    var url = $"https://www.roblox.com/games/start?placeId={placeId}&gameId={targetJobId}";
                    
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });

                    // Auto connect chat
                    _viewModel.JobId = jobId;
                    _viewModel.PlaceId = game.PlaceId;
                    _viewModel.IsServerBrowserVisible = false;
                    
                    // We need a username to connect
                    if (string.IsNullOrWhiteSpace(_viewModel.Username))
                    {
                        // Try to get from logs or prompt
                        var session = await RobloxLogParser.TryReadLatestAsync();
                        if (session != null && !string.IsNullOrWhiteSpace(session.Username))
                        {
                            _viewModel.Username = session.Username;
                            _viewModel.UserId = session.UserId?.ToString() ?? string.Empty;
                        }
                        else
                        {
                            MessageBox.Show("Please enter a username in settings or start Roblox first.", "Username Required");
                            return;
                        }
                    }

                    await DisposeClientAsync();
                    _chatClient = new ChatClient(_viewModel.BackendUrl);
                    _chatClient.MessageReceived += HandleMessageReceived;
                    _chatClient.ParticipantsChanged += HandleParticipantsChanged;
                    _chatClient.HistoryReceived += HandleHistoryReceived;
                    _chatClient.TypingIndicatorReceived += HandleTypingIndicator;

                    var userId = ResolveUserId();
                    await _chatClient.ConnectAsync(_viewModel.Username, _viewModel.JobId, userId, _viewModel.PlaceId);
                    _viewModel.IsConnected = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to join: {ex.Message}");
                }
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

internal sealed record AvatarResolution(long? UserId, string AvatarUrl);
