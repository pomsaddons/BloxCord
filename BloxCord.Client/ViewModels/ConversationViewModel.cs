using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BloxCord.Client.Models;

namespace BloxCord.Client.ViewModels;

public class ConversationViewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _isUnread;
    private bool _isMuted;

    public string Id { get; set; } = string.Empty; // JobId or UserId or GroupId
    public bool IsDirectMessage { get; set; }
    public ObservableCollection<ClientChatMessage> Messages { get; } = new();

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public bool IsUnread
    {
        get => _isUnread;
        set => SetField(ref _isUnread, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
