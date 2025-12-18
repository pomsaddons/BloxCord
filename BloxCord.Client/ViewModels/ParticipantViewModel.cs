using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Diagnostics;
using BloxCord.Client.Helpers;

namespace BloxCord.Client.ViewModels;

public class ParticipantViewModel : INotifyPropertyChanged
{
    private const string OwnerUsername = "p0mp0mpur_1NN";
    private string _avatarUrl = string.Empty;
    private string? _displayName;
    private bool _isTyping;
    private bool _isSelected;
    private string? _countryCode;
    private string? _dmPublicKey;

    public ICommand GoToProfileCommand { get; }

    public ParticipantViewModel()
    {
        GoToProfileCommand = new RelayCommand(_ => OpenProfile());
    }

    private void OpenProfile()
    {
        if (UserId.HasValue)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://www.roblox.com/users/{UserId}/profile",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore errors
            }
        }
    }

    public string Username { get; init; } = string.Empty;

    public bool IsOwner => string.Equals(Username?.Trim(), OwnerUsername, StringComparison.OrdinalIgnoreCase);

    public string? DisplayName
    {
        get => _displayName;
        set
        {
            if (!SetField(ref _displayName, value))
                return;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel)));
        }
    }

    public string DisplayLabel
    {
        get
        {
            var flag = FlagEmoji.FromCountryCode(CountryCode);
            var prefix = string.IsNullOrEmpty(flag) ? string.Empty : flag + " ";

            if (string.IsNullOrWhiteSpace(DisplayName))
                return prefix + Username;

            if (string.Equals(DisplayName.Trim(), Username, StringComparison.OrdinalIgnoreCase))
                return prefix + Username;

            return $"{prefix}{DisplayName} (@{Username})";
        }
    }


    public string? CountryCode
    {
        get => _countryCode;
        set
        {
            if (!SetField(ref _countryCode, value))
                return;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel)));
        }
    }

    public string? DmPublicKey
    {
        get => _dmPublicKey;
        set => SetField(ref _dmPublicKey, value);
    }

    private long? _userId;
    public long? UserId
    {
        get => _userId;
        set
        {
            if (!SetField(ref _userId, value))
                return;
        }
    }

    public string AvatarUrl
    {
        get => _avatarUrl;
        set => SetField(ref _avatarUrl, value);
    }

    public bool IsTyping
    {
        get => _isTyping;
        set => SetField(ref _isTyping, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
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
