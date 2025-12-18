using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BloxCord.Client.Models;
using System.Linq;
using System.Windows.Input;

namespace BloxCord.Client.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string _backendUrl = "http://localhost:5158";
    private string _username = string.Empty;
    private string _jobId = string.Empty;
    private string _statusMessage = "Disconnected";
    private string _outgoingMessage = string.Empty;
    private string _userId = string.Empty;
    private bool _isConnected;
    private string _typingIndicator = string.Empty;
    private bool _typingIndicatorVisible;
    private long? _sessionUserId;
    private int _participantCount;
    private bool _isServerBrowserVisible;
    private GameDto? _selectedGame;
    private ConversationViewModel? _selectedConversation;
    private BannerViewModel? _banner;
    private bool _isBannerVisible;

    private string _countryCode = Services.ConfigService.Current.CountryCode;
    private string _preferredLanguage = Services.ConfigService.Current.PreferredLanguage;

    private string _userToken = Services.ConfigService.Current.UserToken;

    private bool _enableE2eeDirectMessages = Services.ConfigService.Current.EnableE2eeDirectMessages;


    private string? _pinnedMessageId;
    private string _pinnedMessagePreview = string.Empty;

    private string? _replyToMessageId;
    private string _replyPreview = string.Empty;
    private string? _editingMessageId;
    private string _editingPreview = string.Empty;

    public ObservableCollection<ConversationViewModel> Conversations { get; } = new();

    // Proxy property for backward compatibility / ease of binding
    public ObservableCollection<ClientChatMessage> Messages => SelectedConversation?.Messages ?? new ObservableCollection<ClientChatMessage>();

    public ObservableCollection<ParticipantViewModel> Participants { get; } = new();

    public ObservableCollection<GameDto> Games { get; } = new();

    public BannerViewModel? Banner
    {
        get => _banner;
        set => SetField(ref _banner, value);
    }

    public bool IsBannerVisible
    {
        get => _isBannerVisible;
        set => SetField(ref _isBannerVisible, value);
    }

    public string CountryCode
    {
        get => _countryCode;
        set
        {
            if (SetField(ref _countryCode, value))
            {
                Services.ConfigService.Current.CountryCode = value;
                Services.ConfigService.Save();
            }
        }
    }

    public string PreferredLanguage
    {
        get => _preferredLanguage;
        set
        {
            if (SetField(ref _preferredLanguage, value))
            {
                Services.ConfigService.Current.PreferredLanguage = value;
                Services.ConfigService.Save();
            }
        }
    }

    public bool EnableE2eeDirectMessages
    {
        get => _enableE2eeDirectMessages;
        set
        {
            if (SetField(ref _enableE2eeDirectMessages, value))
            {
                Services.ConfigService.Current.EnableE2eeDirectMessages = value;
                Services.ConfigService.Save();
            }
        }
    }


    public string UserToken
    {
        get => _userToken;
        set
        {
            if (SetField(ref _userToken, value))
            {
                Services.ConfigService.Current.UserToken = value;
                Services.ConfigService.Save();
            }
        }
    }

    public ICommand OpenGamePageCommand { get; }
    public ICommand ViewServersCommand { get; }
    public ICommand ClearSelectedGameCommand { get; }
    public ICommand SelectConversationCommand { get; }
    public ICommand CloseConversationCommand { get; }
    public ICommand DismissBannerCommand { get; }

    public MainViewModel()
    {
        // Initialize with Server conversation
        var serverConv = new ConversationViewModel { Title = "Server Chat", Id = "SERVER", IsDirectMessage = false };
        try
        {
            if (Services.ConfigService.Current.MutedConversations.Any(x => string.Equals(x, serverConv.Id, StringComparison.OrdinalIgnoreCase)))
                serverConv.IsMuted = true;
        }
        catch { }
        Conversations.Add(serverConv);
        SelectedConversation = serverConv;

        DismissBannerCommand = new RelayCommand(_ => IsBannerVisible = false);

        SelectConversationCommand = new RelayCommand(param =>
        {
            if (param is ConversationViewModel conv)
            {
                SelectedConversation = conv;
            }
            else if (param is ParticipantViewModel participant)
            {
                if (participant.UserId.HasValue)
                {
                    var dm = GetOrCreateDm(participant.UserId.Value, participant.Username);
                    SelectedConversation = dm;
                }
            }
        });

        CloseConversationCommand = new RelayCommand(param =>
        {
            if (param is ConversationViewModel conv && conv.IsDirectMessage)
            {
                Conversations.Remove(conv);
                if (SelectedConversation == conv)
                {
                    SelectedConversation = Conversations.FirstOrDefault();
                }
            }
        });

        OpenGamePageCommand = new RelayCommand(param =>
        {
            if (param is long placeId)
            {
                try
                {
                    var url = $"https://www.roblox.com/games/{placeId}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        });

        ViewServersCommand = new RelayCommand(param =>
        {
            if (param is GameDto game)
            {
                SelectedGame = game;
            }
        });

        ClearSelectedGameCommand = new RelayCommand(_ => SelectedGame = null);
    }

    public bool IsReplying => !string.IsNullOrEmpty(_replyToMessageId);
    public string ReplyPreview
    {
        get => _replyPreview;
        set => SetField(ref _replyPreview, value);
    }
    public string? ReplyToMessageId
    {
        get => _replyToMessageId;
        set
        {
            if (SetField(ref _replyToMessageId, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsReplying)));
            }
        }
    }

    public bool IsEditing => !string.IsNullOrEmpty(_editingMessageId);
    public string EditingPreview
    {
        get => _editingPreview;
        set => SetField(ref _editingPreview, value);
    }
    public string? EditingMessageId
    {
        get => _editingMessageId;
        set
        {
            if (SetField(ref _editingMessageId, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
            }
        }
    }

    public void ClearComposerContext()
    {
        ReplyToMessageId = null;
        ReplyPreview = string.Empty;
        EditingMessageId = null;
        EditingPreview = string.Empty;
    }

    public bool HasPinnedMessage => !string.IsNullOrEmpty(_pinnedMessageId);
    public string? PinnedMessageId
    {
        get => _pinnedMessageId;
        set
        {
            if (SetField(ref _pinnedMessageId, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPinnedMessage)));
            }
        }
    }

    public string PinnedMessagePreview
    {
        get => _pinnedMessagePreview;
        set => SetField(ref _pinnedMessagePreview, value);
    }

    public GameDto? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetField(ref _selectedGame, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGameSelected)));
            }
        }
    }

    public ConversationViewModel? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (SetField(ref _selectedConversation, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Messages)));
                if (_selectedConversation != null)
                    _selectedConversation.IsUnread = false;
            }
        }
    }

    public bool IsGameSelected => SelectedGame != null;

    public bool IsServerBrowserVisible
    {
        get => _isServerBrowserVisible;
        set => SetField(ref _isServerBrowserVisible, value);
    }

    public string BackendUrl
    {
        get => _backendUrl;
        set => SetField(ref _backendUrl, value);
    }

    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    public string JobId
    {
        get => _jobId;
        set => SetField(ref _jobId, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string OutgoingMessage
    {
        get => _outgoingMessage;
        set => SetField(ref _outgoingMessage, value);
    }

    public string UserId
    {
        get => _userId;
        set => SetField(ref _userId, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetField(ref _isConnected, value);
    }

    private bool _isTestMode;
    public bool IsTestMode
    {
        get => _isTestMode;
        set => SetField(ref _isTestMode, value);
    }

    public string TypingIndicator
    {
        get => _typingIndicator;
        set
        {
            if (SetField(ref _typingIndicator, value))
                IsTypingIndicatorVisible = !string.IsNullOrWhiteSpace(value);
        }
    }

    public bool IsTypingIndicatorVisible
    {
        get => _typingIndicatorVisible;
        private set => SetField(ref _typingIndicatorVisible, value);
    }

    public long? SessionUserId
    {
        get => _sessionUserId;
        set => SetField(ref _sessionUserId, value);
    }

    public long? PlaceId { get; set; }

    public int ParticipantCount
    {
        get => _participantCount;
        set => SetField(ref _participantCount, value);
    }

    public void ResetMessages()
    {
        foreach (var conv in Conversations)
        {
            conv.Messages.Clear();
        }
    }

    public ConversationViewModel GetOrCreateDm(long userId, string username)
    {
        var existing = Conversations.FirstOrDefault(c => c.IsDirectMessage && c.Id == userId.ToString());
        if (existing != null) return existing;

        var newConv = new ConversationViewModel
        {
            Id = userId.ToString(),
            Title = username,
            IsDirectMessage = true
        };

        try
        {
            if (Services.ConfigService.Current.MutedConversations.Any(x => string.Equals(x, newConv.Id, StringComparison.OrdinalIgnoreCase)))
                newConv.IsMuted = true;
        }
        catch { }
        
        Conversations.Add(newConv);
        return newConv;
    }

    public void UpdateTypingIndicator(IEnumerable<string> usernames)
    {
        var list = usernames?.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();

        if (list.Count == 0)
        {
            TypingIndicator = string.Empty;
            return;
        }

        if (list.Count == 1)
        {
            TypingIndicator = $"{list[0]} is typing...";
            return;
        }

        TypingIndicator = $"{string.Join(", ", list)} are typing...";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
