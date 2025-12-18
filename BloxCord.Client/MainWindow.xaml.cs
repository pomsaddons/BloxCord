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
using Microsoft.Toolkit.Uwp.Notifications;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;

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

    private readonly E2eeDmService _e2eeDm = new();
    private readonly Dictionary<long, string> _dmPublicKeysByUserId = new();
    private string? _currentChannelLanguageCode;

    private static List<ReactionBadge>? BuildReactionBadges(Dictionary<string, ReactionBucketDto>? reactions)
    {
        if (reactions is null || reactions.Count == 0)
            return null;

        var list = new List<ReactionBadge>();
        foreach (var kvp in reactions)
        {
            var emoji = kvp.Key;
            var bucket = kvp.Value;
            var count = bucket?.Usernames?.Count ?? 0;
            if (count <= 0) count = bucket?.UserIds?.Count ?? 0;
            if (count <= 0) continue;

            list.Add(new ReactionBadge { Emoji = emoji, Count = count });
        }

        return list.Count == 0 ? null : list;
    }

    private static string? BuildReplyPreview(ConversationViewModel conv, string? replyToId)
    {
        if (string.IsNullOrWhiteSpace(replyToId))
            return null;

        var target = conv.Messages.LastOrDefault(m => string.Equals(m.Id, replyToId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return "Reply";

        return $"{target.Username}: {TrimPreview(target.Content)}";
    }

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

        _viewModel.PropertyChanged += async (_, args) =>
        {
            try
            {
                if (args.PropertyName is nameof(MainViewModel.CountryCode) or nameof(MainViewModel.PreferredLanguage) or nameof(MainViewModel.EnableE2eeDirectMessages))
                {
                    if (!_viewModel.IsConnected || _chatClient is null)
                        return;

                    var dmPublicKey = ConfigService.Current.EnableE2eeDirectMessages ? _e2eeDm.GetPublicKeyBase64() : null;
                    await _chatClient.UpdatePresenceAsync(
                        countryCode: ConfigService.Current.CountryCode,
                        preferredLanguage: ConfigService.Current.PreferredLanguage,
                        dmPublicKey: dmPublicKey);
                }
            }
            catch
            {
                // ignore
            }
        };
        Closed += async (_, _) => 
        {
            await DisposeClientAsync();
            _e2eeDm.Dispose();
        };

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

        _ = FetchBannerAsync();
    }


    private async Task FetchBannerAsync()
    {
        try
        {
            using var client = new HttpClient();
            // Add cache buster
            var url = $"https://raw.githubusercontent.com/pompompur1nn/RoChatBanner/refs/heads/main/banners.json?t={DateTime.UtcNow.Ticks}";
            var json = await client.GetStringAsync(url);

            var currentVersionString = GetCurrentAppVersionString();
            var currentSemver = TryParseSemver(currentVersionString);

            BannerDto? banner = null;

            // Prefer array schema (rotation) when possible.
            try
            {
                var banners = JsonSerializer.Deserialize<BannerDto[]>(json);
                if (banners is not null)
                {
                    foreach (var candidate in banners)
                    {
                        if (candidate is null || !candidate.Enabled)
                            continue;
                        if (!IsBannerEligibleForVersion(candidate, currentVersionString, currentSemver))
                            continue;

                        banner = candidate;
                        break;
                    }
                }
            }
            catch
            {
                // Fall back to single object.
            }

            if (banner is null)
            {
                var single = JsonSerializer.Deserialize<BannerDto>(json);
                if (single is not null && single.Enabled && IsBannerEligibleForVersion(single, currentVersionString, currentSemver))
                    banner = single;
            }

            if (banner is not null)
            {
                _viewModel.Banner = new BannerViewModel(banner);
                _viewModel.IsBannerVisible = true;
            }
        }
        catch
        {
            // Ignore banner fetch errors
        }
    }

    private static string GetCurrentAppVersionString()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational;

            var v = asm.GetName().Version;
            if (v is not null)
                return v.ToString();
        }
        catch
        {
            // ignore
        }

        return "0.0.0";
    }

    private static Version? TryParseSemver(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Accepts 1.2.3 / 1.2.3.4 and tolerates suffixes like 1.2.3+abc.
        var m = Regex.Match(value, @"\b(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?\b");
        if (!m.Success)
            return null;

        try
        {
            var major = int.Parse(m.Groups[1].Value);
            var minor = int.Parse(m.Groups[2].Value);
            var patch = int.Parse(m.Groups[3].Value);
            if (m.Groups[4].Success)
                return new Version(major, minor, patch, int.Parse(m.Groups[4].Value));
            return new Version(major, minor, patch);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBannerEligibleForVersion(BannerDto banner, string currentVersionString, Version? currentSemver)
    {
        if (banner.Versions is { Length: > 0 })
        {
            foreach (var v in banner.Versions)
            {
                if (string.IsNullOrWhiteSpace(v))
                    continue;

                var trimmed = v.Trim();
                if (string.Equals(trimmed, currentVersionString, StringComparison.OrdinalIgnoreCase))
                    return true;

                var parsed = TryParseSemver(trimmed);
                if (parsed is not null && currentSemver is not null && parsed.Equals(currentSemver))
                    return true;
            }

            return false;
        }

        var min = TryParseSemver(banner.MinVersion);
        var max = TryParseSemver(banner.MaxVersion);

        if (min is null && max is null)
            return true;

        if (currentSemver is null)
            return false;

        if (min is not null && currentSemver < min)
            return false;
        if (max is not null && currentSemver > max)
            return false;

        return true;
    }

    public void EnableTestMode()
    {
        var random = new Random();
        var testId = random.Next(1000000, 9999999);
        _viewModel.Username = $"TestUser_{testId}";
        _viewModel.UserId = testId.ToString();
        _viewModel.SessionUserId = testId;
        _viewModel.IsTestMode = true;
        Title += " [TEST MODE]";
        _viewModel.JobId = "TEST_SERVER_JOB_ID";
    }

    private void WireChatClientHandlers(ChatClient client)
    {
        client.MessageReceived -= HandleMessageReceived;
        client.MessageReceived += HandleMessageReceived;

        client.MessageUpdated -= HandleMessageUpdated;
        client.MessageUpdated += HandleMessageUpdated;

        client.ParticipantsChanged -= HandleParticipantsChanged;
        client.ParticipantsChanged += HandleParticipantsChanged;

        client.HistoryReceived -= HandleHistoryReceived;
        client.HistoryReceived += HandleHistoryReceived;

        client.TypingIndicatorReceived -= HandleTypingIndicator;
        client.TypingIndicatorReceived += HandleTypingIndicator;

        client.PrivateMessageReceived -= HandlePrivateMessageReceived;
        client.PrivateMessageReceived += HandlePrivateMessageReceived;

        client.PinnedMessageChanged -= HandlePinnedMessageChanged;
        client.PinnedMessageChanged += HandlePinnedMessageChanged;

        client.PinVoteStateReceived -= HandlePinVoteState;
        client.PinVoteStateReceived += HandlePinVoteState;

        client.KickVoteStateReceived -= HandleKickVoteState;
        client.KickVoteStateReceived += HandleKickVoteState;

        client.Kicked -= HandleKicked;
        client.Kicked += HandleKicked;

        client.LanguageChanged -= HandleLanguageChanged;
        client.LanguageChanged += HandleLanguageChanged;

        client.LanguageVoteStateReceived -= HandleLanguageVoteState;
        client.LanguageVoteStateReceived += HandleLanguageVoteState;

        client.TokenMinted -= HandleTokenMinted;
        client.TokenMinted += HandleTokenMinted;

        client.Banned -= HandleBanned;
        client.Banned += HandleBanned;

        client.AuthFailed -= HandleAuthFailed;
        client.AuthFailed += HandleAuthFailed;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsServerBrowserVisible = false;
        
        if (!_viewModel.IsTestMode)
        {
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
        }
        else
        {
             _viewModel.StatusMessage = "Test Mode Active.";
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
            WireChatClientHandlers(_chatClient);

            _participantLookup.Clear();
            _viewModel.Participants.Clear();
            _viewModel.ResetMessages();

            var userId = ResolveUserId();
            
            if (userId == null && !string.IsNullOrEmpty(_viewModel.Username))
            {
                try 
                {
                    userId = await RobloxUsernameDirectory.TryResolveUserIdAsync(_viewModel.Username);
                    if (userId.HasValue)
                    {
                        _viewModel.SessionUserId = userId;
                    }
                }
                catch
                {
                    // Ignore resolution errors, proceed without ID (DMs might not work)
                }
            }

            var dmPublicKey = ConfigService.Current.EnableE2eeDirectMessages ? _e2eeDm.GetPublicKeyBase64() : null;
            await _chatClient.ConnectAsync(
                _viewModel.Username,
                _viewModel.JobId,
                userId,
                _viewModel.PlaceId,
                countryCode: ConfigService.Current.CountryCode,
                preferredLanguage: ConfigService.Current.PreferredLanguage,
                dmPublicKey: dmPublicKey,
                token: ConfigService.Current.UserToken);

            _viewModel.IsConnected = true;
            _viewModel.StatusMessage = "Connected!";

        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Connect failed: {ex.Message}";
            MessageBox.Show($"Connect failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async Task VoteLanguageAsync(string languageCode)
    {
        if (_chatClient is null)
            throw new InvalidOperationException("Not connected");

        await _chatClient.VoteLanguageAsync(languageCode);
    }

    public async Task RequestTokenMintAsync()
    {
        if (_chatClient is null)
            throw new InvalidOperationException("Not connected");

        await _chatClient.MintTokenAsync();
    }

    private void HandleTokenMinted(object? sender, ChatClient.TokenMintedDto dto)
    {
        Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(dto.Token))
            {
                ConfigService.Current.UserToken = dto.Token;
                ConfigService.Save();

                _viewModel.UserToken = dto.Token;
                _chatClient?.SetToken(dto.Token);

                _viewModel.StatusMessage = "Token minted.";
            }
        });
    }

    private void HandleBanned(object? sender, ChatClient.BannedDto dto)
    {
        Dispatcher.Invoke(async () =>
        {
            var msg = dto.Reason ?? "Banned";
            if (!string.IsNullOrWhiteSpace(dto.AppealUrl))
            {
                msg += $"\nAppeal: {dto.AppealUrl}";
            }
            MessageBox.Show(msg, "Banned", MessageBoxButton.OK, MessageBoxImage.Warning);
            await DisposeClientAsync();
            _viewModel.IsConnected = false;
            _viewModel.StatusMessage = "Disconnected (banned).";
        });
    }

    private void HandleAuthFailed(object? sender, ChatClient.AuthFailedDto dto)
    {
        Dispatcher.Invoke(async () =>
        {
            MessageBox.Show(dto.Reason ?? "Authentication failed", "Auth Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            await DisposeClientAsync();
            _viewModel.IsConnected = false;
            _viewModel.StatusMessage = "Disconnected (auth failed).";
        });
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

        // No channel to update once disconnected.
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

            // Slash commands (minimal UX)
            if (!_viewModel.IsEditing && !_viewModel.IsReplying)
            {
                if (message.StartsWith("/lang ", StringComparison.OrdinalIgnoreCase))
                {
                    var code = message.Substring(6).Trim();
                    if (!string.IsNullOrWhiteSpace(code))
                        await _chatClient.VoteLanguageAsync(code);

                    _viewModel.OutgoingMessage = string.Empty;
                    return;
                }

                if (message.StartsWith("/country ", StringComparison.OrdinalIgnoreCase))
                {
                    var code = message.Substring(9).Trim();
                    ConfigService.Current.CountryCode = code;
                    ConfigService.Save();

                    await _chatClient.UpdatePresenceAsync(countryCode: code);
                    _viewModel.OutgoingMessage = string.Empty;
                    return;
                }
            }

            message = CustomEmojiService.ExpandToRbxassetIds(message);

            if (_viewModel.IsEditing && !string.IsNullOrWhiteSpace(_viewModel.EditingMessageId))
            {
                await _chatClient.EditMessageAsync(_viewModel.EditingMessageId, message);
                _viewModel.ClearComposerContext();
            }
            else if (_viewModel.IsReplying && !string.IsNullOrWhiteSpace(_viewModel.ReplyToMessageId))
            {
                await _chatClient.SendReplyAsync(message, _viewModel.ReplyToMessageId);
                _viewModel.ReplyToMessageId = null;
                _viewModel.ReplyPreview = string.Empty;
            }
            else if (_viewModel.SelectedConversation != null)
            {
                if (_viewModel.SelectedConversation.IsDirectMessage)
                {
                    // Use the new "Game" based DM system (JobId = -TargetUserId)
                    if (long.TryParse(_viewModel.SelectedConversation.Id, out var toUserId))
                    {
                        if (ConfigService.Current.EnableE2eeDirectMessages && _dmPublicKeysByUserId.TryGetValue(toUserId, out var peerKey) && !string.IsNullOrWhiteSpace(peerKey))
                        {
                            message = _e2eeDm.EncryptToEnvelope(peerKey, message);
                        }

                        var targetJobId = toUserId > 0 ? $"-{toUserId}" : toUserId.ToString();
                        await _chatClient.SendToChannelAsync(targetJobId, message);
                    }
                }
                else
                {
                    await _chatClient.SendAsync(message);
                }
            }
            else
            {
                await _chatClient.SendAsync(message);
            }

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

    private void MessageInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != System.Windows.Input.ModifierKeys.Shift)
        {
            e.Handled = true;
            Send_Click(sender, e);
        }
    }

    private async void HandleMessageReceived(object? sender, ChatMessageDto dto)
    {
        var effectiveContent = await GetEffectiveContentAsync(dto);
        var rawWireContent = effectiveContent;
        effectiveContent = CustomEmojiService.ExpandToRbxassetIds(effectiveContent);

        if (ConfigService.Current.EnableE2eeDirectMessages && _e2eeDm.TryDecryptEnvelope(effectiveContent, out var decrypted))
        {
            effectiveContent = decrypted;
        }
        var resolution = await ResolveAvatarAsync(dto.UserId, dto.Username, dto.AvatarUrl);
        var displayName = await ResolveDisplayNameAsync(resolution.UserId ?? dto.UserId, dto.Username);
        var (imageUrls, emojiImageUrls) = await ExtractImageUrlSetsAsync(effectiveContent);
        var isPing = IsPing(effectiveContent);
        var displayContent = SanitizeDisplayedContent(effectiveContent);
        var shownContent = displayContent;

        var senderCountry = _participantLookup.TryGetValue(dto.Username, out var p) ? p.CountryCode : null;

        await Dispatcher.InvokeAsync(() =>
        {
            // Check if this is a DM (Negative JobId)
            if (dto.JobId.StartsWith("-"))
            {
                // Parse the ID to find out who the conversation is with
                if (long.TryParse(dto.JobId.Substring(1), out var otherUserId))
                {
                    // If I sent it, the JobId is -TargetUserId.
                    // If I received it, the JobId is -SenderUserId.
                    // In both cases, the "otherUserId" derived from JobId is the person I am talking to.
                    // Wait.
                    // If I send to -200. Server echoes with JobId -200.
                    // I should see it in conversation with User 200.
                    // If User 200 sends to me (-100). Server sends to me with JobId -200.
                    // I should see it in conversation with User 200.
                    // So in ALL cases, the JobId tells me which conversation it belongs to.
                    // JobId "-200" -> Conversation with User 200.
                    
                    // We need the username. If it's a new conversation, we might not have it.
                    // If it's incoming from someone else, dto.Username is their username.
                    // If it's my own echo, dto.Username is my username.
                    
                    string conversationName = "Unknown";
                    if (dto.UserId != _viewModel.SessionUserId)
                    {
                        conversationName = dto.Username;
                    }
                    else
                    {
                        // It's me. I need to find the name of User 200.
                        // We might have it in cache or existing conversation.
                        var existing = _viewModel.Conversations.FirstOrDefault(c => c.Id == otherUserId.ToString());
                        if (existing != null) conversationName = existing.Title;
                    }

                    var conv = _viewModel.GetOrCreateDm(otherUserId, conversationName);
                    
                    var isContinuation = IsContinuation(conv, dto.Username, dto.Timestamp.LocalDateTime);
                    var replyPreview = BuildReplyPreview(conv, dto.ReplyToId);

                    conv.Messages.Add(new ClientChatMessage
                    {
                        Id = dto.Id,
                        JobId = dto.JobId,
                        Username = dto.Username,
                        DisplayName = displayName,
                        Content = shownContent,
                        RawContent = rawWireContent,
                        TranslatedContent = null,
                        Timestamp = dto.Timestamp.LocalDateTime,
                        UserId = resolution.UserId ?? dto.UserId,
                        CountryCode = senderCountry,
                        AvatarUrl = resolution.AvatarUrl,
                        ImageUrl = imageUrls?.FirstOrDefault(),
                        ImageUrls = imageUrls,
                        EmojiImageUrls = emojiImageUrls,
                        ReplyToId = dto.ReplyToId,
                        ReplyPreview = replyPreview,
                        EditedAt = dto.EditedAt,
                        DeletedAt = dto.DeletedAt,
                        IsSystemMessage = dto.IsSystem ?? false,
                        IsContinuation = isContinuation,
                        Reactions = dto.Reactions?.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new ReactionBucket { Usernames = kvp.Value.Usernames, UserIds = kvp.Value.UserIds }
                        ),
                        ReactionBadges = BuildReactionBadges(dto.Reactions)
                    });

                    if (_viewModel.SelectedConversation != conv)
                    {
                        if (!conv.IsMuted || isPing)
                        {
                            conv.IsUnread = true;
                            ShowNotification($"DM from {dto.Username}", displayContent);
                        }
                    }
                    else if (!IsActive)
                    {
                        if (!conv.IsMuted || isPing)
                            ShowNotification($"DM from {dto.Username}", displayContent);
                    }
                    return;
                }
            }

            // Find Server conversation
            var serverConv = _viewModel.Conversations.FirstOrDefault(c => !c.IsDirectMessage);
            if (serverConv != null)
            {
                var isContinuation = IsContinuation(serverConv, dto.Username, dto.Timestamp.LocalDateTime);
                var replyPreview = BuildReplyPreview(serverConv, dto.ReplyToId);
                serverConv.Messages.Add(new ClientChatMessage
                {
                    Id = dto.Id,
                    Content = shownContent,
                    RawContent = rawWireContent,
                    TranslatedContent = null,
                    JobId = dto.JobId,
                    Username = dto.Username,
                    DisplayName = displayName,
                    Timestamp = dto.Timestamp.LocalDateTime,
                    UserId = resolution.UserId,
                    CountryCode = senderCountry,
                    AvatarUrl = resolution.AvatarUrl,
                    ImageUrl = imageUrls?.FirstOrDefault(),
                    ImageUrls = imageUrls,
                    EmojiImageUrls = emojiImageUrls,
                    ReplyToId = dto.ReplyToId,
                    ReplyPreview = replyPreview,
                    EditedAt = dto.EditedAt,
                    DeletedAt = dto.DeletedAt,
                    IsSystemMessage = dto.IsSystem ?? false,
                    IsContinuation = isContinuation,
                    Reactions = dto.Reactions?.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new ReactionBucket { Usernames = kvp.Value.Usernames, UserIds = kvp.Value.UserIds }
                    ),
                    ReactionBadges = BuildReactionBadges(dto.Reactions)
                });

                if (_viewModel.SelectedConversation != serverConv)
                {
                    if (!serverConv.IsMuted || isPing)
                    {
                        serverConv.IsUnread = true;
                        ShowNotification($"#{serverConv.Title}", $"{dto.Username}: {shownContent}");
                    }
                }
                else if (!IsActive)
                {
                    if (!serverConv.IsMuted || isPing)
                        ShowNotification($"#{serverConv.Title}", $"{dto.Username}: {shownContent}");
                }
            }
        }, DispatcherPriority.Background);
    }

    private async void HandleMessageUpdated(object? sender, ChatMessageDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Id)) return;
        if (dto.JobId.StartsWith("-")) return; // DM updates later

        var effectiveContent = await GetEffectiveContentAsync(dto);
        var rawWireContent = effectiveContent;
        effectiveContent = CustomEmojiService.ExpandToRbxassetIds(effectiveContent);
        if (ConfigService.Current.EnableE2eeDirectMessages && _e2eeDm.TryDecryptEnvelope(effectiveContent, out var decrypted))
        {
            effectiveContent = decrypted;
        }
        var resolution = await ResolveAvatarAsync(dto.UserId, dto.Username, dto.AvatarUrl);
        var displayName = await ResolveDisplayNameAsync(resolution.UserId ?? dto.UserId, dto.Username);
        var (imageUrls, emojiImageUrls) = await ExtractImageUrlSetsAsync(effectiveContent);
        var displayContent = SanitizeDisplayedContent(effectiveContent);
        var shownContent = displayContent;

        var senderCountry = _participantLookup.TryGetValue(dto.Username, out var p) ? p.CountryCode : null;

        await Dispatcher.InvokeAsync(() =>
        {
            var serverConv = _viewModel.Conversations.FirstOrDefault(c => !c.IsDirectMessage);
            if (serverConv == null) return;

            var idx = serverConv.Messages.ToList().FindIndex(m => m.Id == dto.Id);
            if (idx < 0) return;

            // Deleted messages should disappear from the UI.
            if (dto.DeletedAt.HasValue)
            {
                serverConv.Messages.RemoveAt(idx);
                return;
            }

            var replyPreview = BuildReplyPreview(serverConv, dto.ReplyToId);

            serverConv.Messages[idx] = new ClientChatMessage
            {
                Id = dto.Id,
                Content = shownContent,
                RawContent = rawWireContent,
                TranslatedContent = null,
                JobId = dto.JobId,
                Username = dto.Username,
                DisplayName = displayName,
                Timestamp = dto.Timestamp.LocalDateTime,
                UserId = resolution.UserId,
                CountryCode = senderCountry,
                AvatarUrl = resolution.AvatarUrl,
                ImageUrl = imageUrls?.FirstOrDefault(),
                ImageUrls = imageUrls,
                EmojiImageUrls = emojiImageUrls,
                ReplyToId = dto.ReplyToId,
                ReplyPreview = replyPreview,
                EditedAt = dto.EditedAt,
                DeletedAt = dto.DeletedAt,
                IsSystemMessage = dto.IsSystem ?? false,
                IsContinuation = serverConv.Messages.ElementAtOrDefault(idx)?.IsContinuation ?? false,
                Reactions = dto.Reactions?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new ReactionBucket { Usernames = kvp.Value.Usernames, UserIds = kvp.Value.UserIds }
                ),
                ReactionBadges = BuildReactionBadges(dto.Reactions)
            };
        }, DispatcherPriority.Background);
    }

    private static bool IsContinuation(ConversationViewModel conv, string username, DateTime timestamp)
    {
        // group by same sender within 5 minutes
        var last = conv.Messages.LastOrDefault(m => !m.IsSystemMessage);
        if (last is null) return false;
        if (!string.Equals(last.Username, username, StringComparison.OrdinalIgnoreCase)) return false;
        return (timestamp - last.Timestamp).TotalMinutes <= 5;
    }

    private bool IsPing(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        if (string.IsNullOrWhiteSpace(_viewModel.Username)) return false;
        return Regex.IsMatch(content, $@"\B@{Regex.Escape(_viewModel.Username)}\b", RegexOptions.IgnoreCase);
    }

    private static string SanitizeDisplayedContent(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        // Autohide Roblox decal ids
        var withoutIds = Regex.Replace(content, @"rbxassetid://emoji://\d+", string.Empty, RegexOptions.IgnoreCase);
        withoutIds = Regex.Replace(withoutIds, @"emoji://\d+", string.Empty, RegexOptions.IgnoreCase);
        withoutIds = Regex.Replace(withoutIds, @"rbxassetid://(?!emoji://)\d+", string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(withoutIds, @"\s{2,}", " ").Trim();
    }

    private static Task<string> GetEffectiveContentAsync(ChatMessageDto dto)
    {
        // Placeholder for future long-message compression.
        return Task.FromResult(dto.Content ?? string.Empty);
    }

    private void MuteConversation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is ConversationViewModel conv)
        {
            conv.IsMuted = true;
            PersistMutedConversation(conv.Id);
        }
    }

    private void UnmuteConversation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is ConversationViewModel conv)
        {
            conv.IsMuted = false;
            PersistMutedConversation(conv.Id, remove: true);
        }
    }

    private void PersistMutedConversation(string conversationId, bool remove = false)
    {
        try
        {
            var list = ConfigService.Current.MutedConversations ?? new List<string>();
            if (remove)
            {
                list.RemoveAll(x => string.Equals(x, conversationId, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                if (!list.Any(x => string.Equals(x, conversationId, StringComparison.OrdinalIgnoreCase)))
                    list.Add(conversationId);
            }
            ConfigService.Current.MutedConversations = list;
            ConfigService.Save();
        }
        catch { }
    }

    private void PrefillRbxassetId_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_viewModel.OutgoingMessage) && !_viewModel.OutgoingMessage.EndsWith(" "))
                _viewModel.OutgoingMessage += " ";
            _viewModel.OutgoingMessage += "rbxassetid://";

            MessageInput.Focus();
            MessageInput.CaretIndex = MessageInput.Text.Length;
        }
        catch { }
    }

    private void PrefillEmojiAssetId_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_viewModel.OutgoingMessage) && !_viewModel.OutgoingMessage.EndsWith(" "))
                _viewModel.OutgoingMessage += " ";
            _viewModel.OutgoingMessage += "emoji://";

            MessageInput.Focus();
            MessageInput.CaretIndex = MessageInput.Text.Length;
        }
        catch { }
    }

    private void OpenAssetInsertMenu_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button b)
                return;

            if (b.ContextMenu is null)
                return;

            b.ContextMenu.PlacementTarget = b;
            b.ContextMenu.IsOpen = true;
        }
        catch
        {
            // ignore
        }
    }

    private void CancelComposerContext_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearComposerContext();
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is ClientChatMessage m)
        {
            Clipboard.SetText(m.Content ?? string.Empty);
        }
    }

    private void CopyUsername_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is ClientChatMessage m)
        {
            Clipboard.SetText(m.Username ?? string.Empty);
        }
    }

    private void ReplyToMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is ClientChatMessage m)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) return;
            _viewModel.EditingMessageId = null;
            _viewModel.EditingPreview = string.Empty;

            _viewModel.ReplyToMessageId = m.Id;
            _viewModel.ReplyPreview = $"Replying to {m.Username}: {TrimPreview(m.Content)}";
            MessageInput.Focus();
        }
    }

    private async void JumpToReply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button b || b.CommandParameter is not ClientChatMessage m)
                return;
            if (string.IsNullOrWhiteSpace(m.ReplyToId))
                return;

            var conv = _viewModel.SelectedConversation;
            if (conv is null) return;

            var target = conv.Messages.FirstOrDefault(x => string.Equals(x.Id, m.ReplyToId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                _viewModel.StatusMessage = "Original message not found.";
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                MessagesItemsControl?.UpdateLayout();
                var container = MessagesItemsControl?.ItemContainerGenerator.ContainerFromItem(target) as FrameworkElement;
                container?.BringIntoView();
            });
        }
        catch
        {
            // ignore
        }
    }

    private void EditMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is ClientChatMessage m)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) return;
            if (!string.Equals(m.Username, _viewModel.Username, StringComparison.OrdinalIgnoreCase)) return;

            _viewModel.ReplyToMessageId = null;
            _viewModel.ReplyPreview = string.Empty;

            _viewModel.EditingMessageId = m.Id;
            _viewModel.EditingPreview = $"Editing: {TrimPreview(m.Content)}";
            _viewModel.OutgoingMessage = m.Content;
            MessageInput.Focus();
            MessageInput.CaretIndex = MessageInput.Text.Length;
        }
    }

    private async void DeleteMessage_Click(object sender, RoutedEventArgs e)
    {
        if (_chatClient is null) return;
        if (sender is MenuItem mi && mi.CommandParameter is ClientChatMessage m)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) return;
            if (!string.Equals(m.Username, _viewModel.Username, StringComparison.OrdinalIgnoreCase)) return;
            await _chatClient.DeleteMessageAsync(m.Id);
        }
    }

    private async void ReactThumbsUp_Click(object sender, RoutedEventArgs e) => await ReactQuickAsync(sender, "👍");
    private async void ReactHeart_Click(object sender, RoutedEventArgs e) => await ReactQuickAsync(sender, "❤️");
    private async void ReactLaugh_Click(object sender, RoutedEventArgs e) => await ReactQuickAsync(sender, "😂");

    private async Task ReactQuickAsync(object sender, string emoji)
    {
        if (_chatClient is null) return;
        if (sender is MenuItem mi && mi.CommandParameter is ClientChatMessage m)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) return;
            await _chatClient.AddReactionAsync(m.Id, emoji);
        }
    }

    private static string TrimPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= 80 ? t : t.Substring(0, 77) + "...";
    }

    private async void HandlePrivateMessageReceived(object? sender, PrivateMessageDto dto)
    {
        // Determine the other party
        // If I sent it, the other party is ToUserId. If I received it, the other party is FromUserId.
        long otherUserId;
        string otherUsername;
        
        // We need to know our own UserId. It's in _viewModel.SessionUserId
        var myUserId = _viewModel.SessionUserId;

        if (dto.FromUserId == myUserId)
        {
            otherUserId = dto.ToUserId;
            // If we are DMing ourselves, we know the username
            if (otherUserId == myUserId)
            {
                otherUsername = dto.FromUsername;
            }
            else
            {
                otherUsername = "Unknown"; // Placeholder, will be updated if conversation exists
            }
        }
        else
        {
            otherUserId = dto.FromUserId;
            otherUsername = dto.FromUsername;
        }

        var resolution = await ResolveAvatarAsync(dto.FromUserId, dto.FromUsername, null);
        var effectiveContent = await GetEffectiveContentAsync(new ChatMessageDto { Content = dto.Content });
        var rawWireContent = effectiveContent;
        effectiveContent = CustomEmojiService.ExpandToRbxassetIds(effectiveContent);

        if (ConfigService.Current.EnableE2eeDirectMessages && _e2eeDm.TryDecryptEnvelope(effectiveContent, out var decrypted))
        {
            effectiveContent = decrypted;
        }
        var (imageUrls, emojiImageUrls) = await ExtractImageUrlSetsAsync(effectiveContent);
        var displayContent = SanitizeDisplayedContent(effectiveContent);
        var shownContent = displayContent;
        var isPing = IsPing(effectiveContent);
        var senderCountry = _participantLookup.TryGetValue(dto.FromUsername, out var p) ? p.CountryCode : null;

        await Dispatcher.InvokeAsync(() =>
        {
            var conv = _viewModel.GetOrCreateDm(otherUserId, otherUsername);
            // If we just created it and didn't know the username (outgoing case), we might want to update title if possible
            // But for outgoing, we usually create conversation BEFORE sending.
            
            if (conv.Title == "Unknown" && !string.IsNullOrEmpty(otherUsername) && otherUsername != "Unknown")
            {
                conv.Title = otherUsername;
            }

            conv.Messages.Add(new ClientChatMessage
            {
                Content = shownContent,
                RawContent = rawWireContent,
                TranslatedContent = null,
                Username = dto.FromUsername,
                Timestamp = dto.Timestamp.LocalDateTime,
                UserId = dto.FromUserId,
                CountryCode = senderCountry,
                AvatarUrl = resolution.AvatarUrl,
                IsSystemMessage = false,
                ImageUrl = imageUrls?.FirstOrDefault(),
                ImageUrls = imageUrls,
                EmojiImageUrls = emojiImageUrls,
                JobId = "DM"
            });

            if (_viewModel.SelectedConversation != conv)
            {
                if (!conv.IsMuted || isPing)
                {
                    conv.IsUnread = true;
                    ShowNotification($"DM from {dto.FromUsername}", shownContent);
                }
            }
            else if (!IsActive)
            {
                if (!conv.IsMuted || isPing)
                    ShowNotification($"DM from {dto.FromUsername}", shownContent);
            }
        });
    }

    private async Task<(List<string>? DecalUrls, List<string>? EmojiUrls)> ExtractImageUrlSetsAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return (null, null);

        // Separate channels:
        // - emoji://<id> or rbxassetid://emoji://<id> => small emoji rendering
        // - rbxassetid://<id> or roblox.com/library/<id> => regular decal rendering

        var emojiIds = new List<long>();
        foreach (Match m in Regex.Matches(content, @"rbxassetid://emoji://(\d+)", RegexOptions.IgnoreCase))
        {
            if (long.TryParse(m.Groups[1].Value, out var id))
                emojiIds.Add(id);
        }

        foreach (Match m in Regex.Matches(content, @"emoji://(\d+)", RegexOptions.IgnoreCase))
        {
            if (long.TryParse(m.Groups[1].Value, out var id))
                emojiIds.Add(id);
        }

        var decalIds = new List<long>();
        foreach (Match m in Regex.Matches(content, @"rbxassetid://(?!emoji://)(\d+)", RegexOptions.IgnoreCase))
        {
            if (long.TryParse(m.Groups[1].Value, out var id))
                decalIds.Add(id);
        }

        foreach (Match m in Regex.Matches(content, @"roblox\.com/library/(\d+)", RegexOptions.IgnoreCase))
        {
            if (long.TryParse(m.Groups[1].Value, out var id))
                decalIds.Add(id);
        }

        var emojiDistinct = emojiIds.Distinct().Take(16).ToList();
        var decalDistinct = decalIds.Distinct().Take(6).ToList();

        List<string>? resolvedEmoji = null;
        if (emojiDistinct.Count > 0)
        {
            var emojiTasks = emojiDistinct.Select(RobloxAssetService.ResolveDecalAsync).ToArray();
            var emojiResolved = await Task.WhenAll(emojiTasks);
            resolvedEmoji = emojiResolved.Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url!).ToList();
            if (resolvedEmoji.Count == 0) resolvedEmoji = null;
        }

        List<string>? resolvedDecals = null;
        if (decalDistinct.Count > 0)
        {
            var decalTasks = decalDistinct.Select(RobloxAssetService.ResolveDecalAsync).ToArray();
            var decalResolved = await Task.WhenAll(decalTasks);
            resolvedDecals = decalResolved.Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url!).ToList();
            if (resolvedDecals.Count == 0) resolvedDecals = null;
        }

        return (resolvedDecals, resolvedEmoji);
    }

    private void ShowNotification(string title, string content)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(content);

            var iconPath = System.IO.Path.GetFullPath("rochatlogo.png");
            if (System.IO.File.Exists(iconPath))
            {
                builder.AddAppLogoOverride(new Uri(iconPath), ToastGenericAppLogoCrop.Circle);
            }

            builder.Show();
        }
        catch { }
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

                var serverConv = _viewModel.Conversations.FirstOrDefault(c => !c.IsDirectMessage);

                foreach (var username in toRemove)
                {
                    if (_participantLookup.TryGetValue(username, out var existingVm))
                    {
                        _participantLookup.Remove(username);
                        _viewModel.Participants.Remove(existingVm);
                        
                        // Add System Message
                        if (serverConv != null)
                        {
                            serverConv.Messages.Add(new ClientChatMessage
                            {
                                Username = "System",
                                Content = $"{username} left the chat",
                                Timestamp = DateTime.Now,
                                JobId = _viewModel.JobId,
                                IsSystemMessage = true
                            });
                        }
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
                            AvatarUrl = dto.AvatarUrl ?? string.Empty,
                            CountryCode = dto.CountryCode,
                            DmPublicKey = dto.DmPublicKey
                        };

                        _participantLookup[dto.Username] = vm;
                        InsertParticipantSorted(vm);
                        _ = EnsureAvatarForParticipantAsync(vm);

                        // Add System Message (only if we are already connected to avoid spam on initial load)
                        if (_viewModel.IsConnected && serverConv != null) 
                        {
                            serverConv.Messages.Add(new ClientChatMessage
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
                        if (vm.UserId != dto.UserId || vm.AvatarUrl != dto.AvatarUrl || vm.CountryCode != dto.CountryCode || vm.DmPublicKey != dto.DmPublicKey)
                        {
                            vm.UserId = dto.UserId;
                            vm.AvatarUrl = dto.AvatarUrl ?? string.Empty;
                            vm.CountryCode = dto.CountryCode;
                            vm.DmPublicKey = dto.DmPublicKey;
                            _ = EnsureAvatarForParticipantAsync(vm);
                        }
                    }

                    if (dto.UserId.HasValue && !string.IsNullOrWhiteSpace(dto.DmPublicKey))
                    {
                        _dmPublicKeysByUserId[dto.UserId.Value] = dto.DmPublicKey!;
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
        var serverConv = _viewModel.Conversations.FirstOrDefault(c => !c.IsDirectMessage);
        if (serverConv == null) return;

        var ordered = history
            .OrderBy(m => m.Timestamp)
            .ToList();

        var prepared = new List<ClientChatMessage>();

        foreach (var entry in ordered)
        {
            if (entry.DeletedAt.HasValue)
                continue;

            var effectiveContent = await GetEffectiveContentAsync(entry);
            var rawWireContent = effectiveContent;
            effectiveContent = CustomEmojiService.ExpandToRbxassetIds(effectiveContent);

            if (ConfigService.Current.EnableE2eeDirectMessages && _e2eeDm.TryDecryptEnvelope(effectiveContent, out var decrypted))
            {
                effectiveContent = decrypted;
            }

            var displayContent = SanitizeDisplayedContent(effectiveContent);
            var shownContent = displayContent;

            var senderCountry = _participantLookup.TryGetValue(entry.Username, out var p) ? p.CountryCode : null;
            var resolution = await ResolveAvatarAsync(entry.UserId, entry.Username, entry.AvatarUrl);
            var displayName = await ResolveDisplayNameAsync(resolution.UserId ?? entry.UserId, entry.Username);
            var (imageUrls, emojiImageUrls) = await ExtractImageUrlSetsAsync(effectiveContent);

            prepared.Add(new ClientChatMessage
            {
                Id = entry.Id,
                Content = shownContent,
                RawContent = rawWireContent,
                TranslatedContent = null,
                JobId = entry.JobId,
                Username = entry.Username,
                DisplayName = displayName,
                Timestamp = entry.Timestamp.LocalDateTime,
                UserId = resolution.UserId,
                CountryCode = senderCountry,
                AvatarUrl = resolution.AvatarUrl,
                ImageUrl = imageUrls?.FirstOrDefault(),
                ImageUrls = imageUrls,
                EmojiImageUrls = emojiImageUrls,
                ReplyToId = entry.ReplyToId,
                ReplyPreview = null,
                EditedAt = entry.EditedAt,
                DeletedAt = entry.DeletedAt,
                IsSystemMessage = entry.IsSystem ?? false,
                Reactions = entry.Reactions?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new ReactionBucket { Usernames = kvp.Value.Usernames, UserIds = kvp.Value.UserIds }
                ),
                ReactionBadges = BuildReactionBadges(entry.Reactions)
            });
        }

        await Dispatcher.InvokeAsync(() =>
        {
            serverConv.Messages.Clear();

            foreach (var message in prepared)
                serverConv.Messages.Add(message);

            // Fill reply previews now that the conversation list exists.
            for (var i = 0; i < serverConv.Messages.Count; i++)
            {
                var msg = serverConv.Messages[i];
                if (string.IsNullOrWhiteSpace(msg.ReplyToId))
                    continue;

                var rp = BuildReplyPreview(serverConv, msg.ReplyToId);
                if (rp is null)
                    continue;

                serverConv.Messages[i] = new ClientChatMessage
                {
                    Id = msg.Id,
                    Username = msg.Username,
                    DisplayName = msg.DisplayName,
                    CountryCode = msg.CountryCode,
                    Content = msg.Content,
                    Timestamp = msg.Timestamp,
                    JobId = msg.JobId,
                    UserId = msg.UserId,
                    AvatarUrl = msg.AvatarUrl,
                    ImageUrl = msg.ImageUrl,
                    ImageUrls = msg.ImageUrls,
                    EmojiImageUrls = msg.EmojiImageUrls,
                    IsSystemMessage = msg.IsSystemMessage,
                    IsContinuation = msg.IsContinuation,
                    RawContent = msg.RawContent,
                    TranslatedContent = msg.TranslatedContent,
                    ReplyToId = msg.ReplyToId,
                    ReplyPreview = rp,
                    EditedAt = msg.EditedAt,
                    DeletedAt = msg.DeletedAt,
                    Reactions = msg.Reactions,
                    ReactionBadges = msg.ReactionBadges
                };
            }

            // Update pinned preview based on current messages (if we already received it)
            if (!string.IsNullOrWhiteSpace(_viewModel.PinnedMessageId))
            {
                var pinned = serverConv.Messages.FirstOrDefault(m => m.Id == _viewModel.PinnedMessageId);
                if (pinned != null)
                {
                    _viewModel.PinnedMessagePreview = $"{pinned.Username}: {TrimPreview(pinned.Content)}";
                }
            }

            // Add Safety Warning
            serverConv.Messages.Add(new ClientChatMessage
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

    private void HandlePinnedMessageChanged(object? sender, ChatClient.PinnedMessageChangedDto dto)
    {
        Dispatcher.Invoke(() =>
        {
            _viewModel.PinnedMessageId = dto.PinnedMessageId;

            var serverConv = _viewModel.Conversations.FirstOrDefault(c => !c.IsDirectMessage);
            if (serverConv != null && !string.IsNullOrWhiteSpace(dto.PinnedMessageId))
            {
                var pinned = serverConv.Messages.FirstOrDefault(m => m.Id == dto.PinnedMessageId);
                if (pinned != null)
                {
                    _viewModel.PinnedMessagePreview = $"{pinned.Username}: {TrimPreview(pinned.Content)}";
                }
            }
        });
    }

    private void HandlePinVoteState(object? sender, ChatClient.PinVoteUpdateDto dto)
    {
        // Minimal UX: surface state in StatusMessage
        Dispatcher.Invoke(() =>
        {
            if (dto.ActivePinVote?.Voters != null)
            {
                _viewModel.StatusMessage = $"Pin vote: {dto.ActivePinVote.Voters.Count} votes";
            }
        });
    }

    private void HandleKickVoteState(object? sender, ChatClient.KickVoteUpdateDto dto)
    {
        Dispatcher.Invoke(() =>
        {
            if (dto.ActiveKickVote != null)
            {
                _viewModel.StatusMessage = $"Kick vote: {dto.ActiveKickVote.TargetUsername} ({dto.ActiveKickVote.Voters.Count} votes)";
            }
        });
    }

    private void HandleKicked(object? sender, ChatClient.KickedDto dto)
    {
        Dispatcher.Invoke(() =>
        {
            ShowNotification("Kicked", dto.Reason);
        });
    }

    private void HandleLanguageChanged(object? sender, ChatClient.LanguageChangedDto dto)
    {
        _currentChannelLanguageCode = dto.LanguageCode;

        Dispatcher.Invoke(() =>
        {
            _viewModel.StatusMessage = $"Language: {dto.LanguageCode}";

            var serverConv = _viewModel.Conversations.FirstOrDefault(c => !c.IsDirectMessage);
            if (serverConv != null)
            {
                serverConv.Messages.Add(new ClientChatMessage
                {
                    Username = "System",
                    Content = $"Language is now {dto.LanguageCode}.",
                    Timestamp = DateTime.Now,
                    JobId = _viewModel.JobId,
                    IsSystemMessage = true,
                    AvatarUrl = string.Empty
                });
            }
        });
    }

    private void HandleLanguageVoteState(object? sender, ChatClient.LanguageVoteStateDto dto)
    {
        Dispatcher.Invoke(() =>
        {
            _viewModel.StatusMessage = $"Language vote: {dto.LanguageCode}";
        });
    }

    private async void VotePinMessage_Click(object sender, RoutedEventArgs e)
    {
        if (_chatClient is null) return;
        if (sender is MenuItem mi && mi.CommandParameter is ClientChatMessage m)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) return;
            await _chatClient.VotePinAsync(m.Id);
        }
    }

    private async void VoteKick_Click(object sender, RoutedEventArgs e)
    {
        if (_chatClient is null) return;
        if (sender is MenuItem mi && mi.CommandParameter is ParticipantViewModel p)
        {
            if (string.IsNullOrWhiteSpace(p.Username)) return;
            await _chatClient.VoteKickAsync(p.Username);
        }
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

        var effectiveUserId = resolution.UserId ?? participant.UserId;
        var displayName = await ResolveDisplayNameAsync(effectiveUserId, participant.Username);
        await Dispatcher.InvokeAsync(() =>
        {
            participant.UserId ??= resolution.UserId;

            if (!string.IsNullOrEmpty(resolution.AvatarUrl))
                participant.AvatarUrl = resolution.AvatarUrl;

            if (!string.IsNullOrWhiteSpace(displayName))
                participant.DisplayName = displayName;
        }, DispatcherPriority.Background);
    }

    private static string? NormalizeDisplayName(string? displayName)
        => string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

    private static bool DisplayNameDiffers(string? displayName, string username)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        return !string.Equals(displayName.Trim(), username, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ResolveDisplayNameAsync(long? userId, string username)
    {
        if (!userId.HasValue)
            return null;

        var record = await RobloxUserDirectory.TryGetUserAsync(userId.Value);
        if (record is null)
            return null;

        var candidate = NormalizeDisplayName(record.DisplayName);
        return DisplayNameDiffers(candidate, username) ? candidate : null;
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
        if (games is null) return;

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
                    WireChatClientHandlers(_chatClient);

                    _participantLookup.Clear();
                    _viewModel.Participants.Clear();
                    _viewModel.ResetMessages();

                    _viewModel.StatusMessage = "Connecting to chat server...";

                    var userId = ResolveUserId();
                    var dmPublicKey = ConfigService.Current.EnableE2eeDirectMessages ? _e2eeDm.GetPublicKeyBase64() : null;
                    await _chatClient.ConnectAsync(
                        _viewModel.Username,
                        _viewModel.JobId,
                        userId,
                        _viewModel.PlaceId,
                        countryCode: ConfigService.Current.CountryCode,
                        preferredLanguage: ConfigService.Current.PreferredLanguage,
                        dmPublicKey: dmPublicKey,
                        token: ConfigService.Current.UserToken);
                    _viewModel.IsConnected = true;
                    _viewModel.StatusMessage = "Connected!";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to join: {ex.Message}");
                }
            }
        }
    }

    private void NewConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_chatClient == null || !_viewModel.IsConnected)
        {
            MessageBox.Show("You must be connected to a server to search for users.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var searchWindow = new UserSearchWindow(_chatClient)
        {
            Owner = this
        };

        if (searchWindow.ShowDialog() == true && searchWindow.SelectedUser != null)
        {
            var user = searchWindow.SelectedUser;
            // Check if we already have a conversation with this user
            var existingConv = _viewModel.Conversations.FirstOrDefault(c => c.IsDirectMessage && c.Title == user.Username);
            
            if (existingConv != null)
            {
                _viewModel.SelectedConversation = existingConv;
            }
            else
            {
                // Create new DM conversation
                if (user.UserId.HasValue)
                {
                     var conv = _viewModel.GetOrCreateDm(user.UserId.Value, user.Username);
                     _viewModel.SelectedConversation = conv;
                }
                else
                {
                     MessageBox.Show("Could not determine User ID for selected user.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

internal sealed record AvatarResolution(long? UserId, string AvatarUrl);
