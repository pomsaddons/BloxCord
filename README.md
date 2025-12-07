# RoChat

A modern, lightweight chat client for Roblox servers. RoChat allows you to chat with other players in the same server instance without being in the game itself.

![RoChat Logo](BloxCord.Client/rochatlogo.png)

## Features

*   **Server Browser:** View active games and server instances with live player counts and avatars.
*   **Instance Chat:** Connect to a specific server instance (`JobId`) and chat with other RoChat users in that server.
*   **Roblox Integration:** Automatically detects your current Roblox session from local logs.
*   **Modern UI:** Sleek, dark-themed WPF interface with customizable gradients and solid colors.
*   **Safety First:** Includes unmoderated chat warnings and browser-based game launching for security.
*   **Cross-Platform Backend:** Powered by a Node.js/Socket.IO server.

## Projects

| Project | Description |
| --- | --- |
| `BloxCord.Client` | **RoChat Client**. A .NET 9 WPF desktop application. It parses Roblox logs to find your current game, connects to the backend via Socket.IO, and provides a rich UI for chatting and browsing servers. |
| `BloxCord.Server` | **Backend Server**. A Node.js application using Socket.IO to manage chat rooms (channels), handle messaging, and fetch game details from the Roblox API. |

## Prerequisites

*   **Client:** .NET 9 SDK (Windows only for WPF).
*   **Server:** Node.js (v18+).
*   **Roblox:** Installed locally to detect active sessions.

## Build & Run

### 1. Start the Backend Server

```powershell
cd BloxCord.Server
npm install
npm start
```
*The server runs on port 5158 by default.*

### 2. Run the Client

```powershell
dotnet run --project BloxCord.Client/BloxCord.Client.csproj
```

## Configuration

The client creates a `config.json` file on first run. You can modify it to change themes or the backend URL.

```json
{
  "BackendUrl": "https://rochat.pompompurin.tech",
  "Username": "",
  "UseGradient": true,
  "SolidColor": "#0F172A",
  "GradientStart": "#0F172A",
  "GradientEnd": "#334155"
}
```

## Usage

1.  **Launch RoChat.**
2.  **Browse Games:** Use the "Browse" button to see active games where other RoChat users are chatting.
3.  **Join a Chat:** Click on a server instance to join its chat room. You can also click "Join Server" to launch Roblox and join that specific server.
4.  **Automatic Connection:** If you are already playing Roblox, click "Connect" on the main screen to automatically detect your current game and join the chat.

## Disclaimer

RoChat is a third-party application and is not affiliated with Roblox Corporation. Chat is unmoderated; please use caution when sharing personal information.


