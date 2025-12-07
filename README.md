# BloxCord

A lightweight chat stack that creates one SignalR channel per Roblox jobId.
## Projects

| Project | Description |
| --- | --- |
| `BloxCord.Api` | ASP.NET Core (net8.0) backend with `/api/channels` REST endpoints and `/hubs/chat` SignalR hub. Keeps in-memory channel/participant state indexed by jobId. |
| `BloxCord.Client` | WPF (net9.0-windows) desktop client. Reads `%LOCALAPPDATA%\Roblox\logs` to grab jobId + userId the same way as `Bloxstrap/Integrations/ActivityWatcher.cs`, resolves the username via `https://users.roblox.com/v1/users/{id}` (as seen in `Bloxstrap/Models/Entities/UserDetails.cs`), fetches avatar headshots from the Roblox thumbnails API, and connects to the backend via SignalR. |

## Prerequisites

- .NET SDK 9.0.300 (ships with .NET 8 target packs and WPF tooling)
- Roblox installed locally so `%LOCALAPPDATA%/Roblox/logs` exists when parsing sessions

## Build & Run

```powershell
# Restore + build everything
 dotnet build BloxCord.sln

# Start the chat backend on http://localhost:5158
 dotnet run --project BloxCord.Api

# In a new terminal, launch the WPF client
 dotnet run --project BloxCord.Client
```

## Using the app

1. Launch a Roblox experience so that a fresh `Player` log is produced.
2. In the WPF app, click **Load Roblox Session**. The app reuses Bloxstrap's regex patterns to find the latest `! Joining game ...` entry for the jobId and the `universeid:...userid:...` entry for the userId, then calls the Roblox Users API to obtain your username.
3. Review/edit the backend URL (defaults to `http://localhost:5158`), username, or jobId if needed.
4. Click **Connect** to create/join the jobId channel. Every participant in the same jobId shares the stream; **Send** broadcasts to everyone connected to that channel.
5. Use **Disconnect** to leave the SignalR group.

## Implementation notes

- Channel state lives in-memory via `ChannelRegistry`; history is capped at 200 messages.
- Each SignalR connection calls `JoinChannel`, which mirrors the REST call to `/api/channels`. The hub emits `ChannelSnapshot`, `ParticipantsChanged`, and `ReceiveMessage` events.
- Typing notifications flow through the new `NotifyTyping` hub method and `TypingIndicator` broadcast. The client shows "typing..." hints inline plus badges on the participant list.
- The client relies on `RobloxLogParser.TryReadLatestAsync` and `RobloxUserDirectory.TryGetUsernameAsync` to reproduce the same jobId/username discovery approach that Bloxstrap already uses, and `RobloxAvatarDirectory.TryGetHeadshotUrlAsync` to keep the rounded dark UI stocked with Roblox profile pictures.
- Swagger UI is available at `http://localhost:5158/swagger` for quick testing.


