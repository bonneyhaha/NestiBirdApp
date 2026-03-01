# Nesti Bird Assistant

A lightweight Windows desktop notification assistant built with **WPF / C# / .NET 8**. Nesti lives in the bottom-right corner of your screen as a transparent, always-on-top overlay. It connects to a WebSocket server to receive real-time notifications, which it displays as animated cards with sound.

---

## Features

- **Transparent overlay** — no taskbar entry, no title bar, fully click-through on empty areas
- **Animated bird mascot** — sits in the corner, greets you by name on hover
- **Real-time notifications** — WebSocket connection with automatic exponential back-off reconnection
- **Notification cards** — slide in from the right with glassmorphism styling
- **Snooze** — calls a configurable MARS API to snooze a notification
- **Dismiss** — calls a mark-as-read API and stores dismissed IDs locally so they never reappear
- **Sound** — plays a bird chirp on every new notification (MP3, toggleable)
- **Hide / Close controls** — PNG icon buttons appear on bird hover; hide until next notification or exit entirely
- **Dummy test mode** — fires rotating fake notifications on a timer, no server needed
- **Zero-recompile config** — all URLs, timeouts, and flags live in a `.env` file

---

## Screenshots / Layout

```
┌─────────────────────────────────────────────────────────────────┐
│                                              (transparent)       │
│                                                                   │
│                                  ┌───────────────────────────┐   │
│                                  │ 🔔 Notification Title      ⏰ ✕│
│                                  │ Notification body text here   │
│                                  └───────────────────────────┘   │
│                                                                   │
│                                              ┌──────────────┐    │
│                                              │  [–]    [✕]  │    │  ← hover buttons
│                                              │  🐦 (bird)   │    │
│                                              └──────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

Hover over the bird to reveal **Hide** and **Close** buttons.
Hover over a notification card to reveal **Snooze** (⏰) and **Dismiss** (✕) buttons.

---

## Tech Stack

| Layer | Technology |
|---|---|
| UI framework | WPF (Windows Presentation Foundation) |
| Language | C# 12 |
| Runtime | .NET 8 (Windows) |
| Animated GIFs | [WpfAnimatedGif](https://github.com/XamlAnimatedGif/WpfAnimatedGif) 2.0.0 |
| Config parsing | [DotNetEnv](https://github.com/motdotla/dotnet-env) 3.1.1 |
| WebSocket | `System.Net.WebSockets.ClientWebSocket` |
| Audio | `System.Windows.Media.MediaPlayer` (MP3) |

---

## Project Structure

```
Nesti/                          ← Solution root
├── document.txt                ← Detailed technical documentation
├── README.md                   ← This file
├── Nesti.slnx
└── Nesti/                      ← C# project
    ├── .env                    ← All runtime configuration
    ├── Nesti.csproj
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml         ← Main transparent window UI
    ├── MainWindow.xaml.cs      ← Orchestration, click-through, WS wiring
    ├── assets/
    │   ├── nest_bird.gif               ← Bird mascot animation
    │   ├── jarvis.gif                  ← Card icon animation
    │   ├── minimize_button_icon.png    ← Bird hide button
    │   ├── cross_button_notif.png      ← Bird close button
    │   ├── snooze_icon.png             ← Card snooze button
    │   ├── cross_button_notif_icon.png ← Card dismiss button
    │   └── bird_chirp.mp3              ← Notification sound
    ├── Models/
    │   └── NotificationMessage.cs
    ├── Helpers/
    │   ├── AppConfig.cs        ← Reads .env into typed properties
    │   └── UserHelper.cs       ← Name resolution, greeting text
    ├── Services/
    │   ├── IWebSocketSource.cs         ← Interface for real/dummy service
    │   ├── WebSocketService.cs         ← Real WebSocket with reconnection
    │   ├── DummyWebSocketService.cs    ← Test mode notification generator
    │   ├── SoundService.cs             ← MP3 playback
    │   └── NotificationApiService.cs   ← Snooze + mark-as-read HTTP calls
    └── Controls/
        ├── NotificationControl.xaml    ← Notification card UI
        └── NotificationControl.xaml.cs ← Card animations + API wiring
```

---

## Quick Start

### Prerequisites

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Run in development

```bash
# Clone / navigate to project folder
cd "POC-2-WPF/Nesti/Nesti"

# Restore NuGet packages
dotnet restore

# Build
dotnet build --configuration Debug

# Run
dotnet run
```

### Test without a WebSocket server

Edit `Nesti/Nesti/.env`:

```env
USE_REAL_WEBSOCKET=false
DUMMY_INTERVAL_MS=3000
```

Fake notifications fire every 3 seconds automatically.

---

## Configuration (`.env`)

All settings live in `Nesti/Nesti/.env`. The file is copied next to the exe on every build — **no recompile needed** to change any value.

| Key | Default | Description |
|---|---|---|
| `WS_URL` | `wss://localhost:8080/ws` | WebSocket endpoint |
| `WS_RECONNECT_BASE_MS` | `2000` | Initial reconnect delay (ms), doubles each attempt |
| `WS_RECONNECT_MAX_ATTEMPTS` | `10` | Max reconnect attempts before giving up |
| `API_BASE_URL` | *(blank)* | Base URL for REST API calls |
| `API_GET_WS_URL_PATH` | *(blank)* | Path to resolve WebSocket URL per user |
| `API_GET_FULLNAME_PATH` | *(blank)* | Path to resolve user's display name |
| `BIRD_DEFAULT_URL` | *(blank)* | URL opened when bird is clicked |
| `NOTIFICATION_DURATION_MS` | `10000` | How long cards stay on screen |
| `MAX_NOTIFICATIONS` | `5` | Max cards visible simultaneously |
| `SOUND_ENABLED` | `true` | Enable/disable chirp sound |
| `USE_REAL_WEBSOCKET` | `true` | `false` = dummy test mode |
| `DUMMY_INTERVAL_MS` | `5000` | Fake notification interval in test mode |
| `MARS_SNOOZE_URL` | *(blank)* | POST endpoint for snooze action |
| `MARK_AS_READ_URL` | *(blank)* | POST endpoint for mark-as-read action |

---

## Notification Card Actions

| Button | Icon | What it does |
|---|---|---|
| **Snooze** | `snooze_icon.png` | `POST MARS_SNOOZE_URL { "id": "…" }` — slides card out |
| **Dismiss** | `cross_button_notif_icon.png` | `POST MARK_AS_READ_URL` + stores ID in `%AppData%\Nesti\dismissed.json` — slides card out |
| **Card body** | — | Opens `url` from the notification payload in default browser |

Dismissed IDs are persisted locally so the same notification never reappears after a restart.

---

## WebSocket Message Format

Nesti expects JSON text frames with the following shape:

```json
{
  "id": "unique-notification-id",
  "instanceId": "optional-instance-id",
  "title": "Notification Title",
  "message": "Short body text",
  "description": "Alternative body (used if message is absent)",
  "url": "https://link-to-open-on-click.com",
  "type": "info",
  "notificationType": "task",
  "user_id": "username",
  "timestamp": "2025-01-01T12:00:00Z"
}
```

Deduplication is based on `id + instanceId`. Duplicate messages are silently dropped.

---

## Bird Controls

| Button | Icon | What it does |
|---|---|---|
| **Hide** | `minimize_button_icon.png` | Hides bird + panel. Restores automatically on next notification |
| **Close** | `cross_button_notif.png` | Exits Nesti completely |

Both buttons are revealed only on bird hover. They live inside the bird container's layout bounds, so hover detection is reliable across the full hit area.

---

## Build for Production

### Framework-dependent (requires .NET 8 on target)

```bash
dotnet publish -c Release -r win-x64
```

### Self-contained single EXE (~60 MB, no .NET required)

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

> **Deploy alongside the exe:**
> - `.env` — configuration file
> - `assets/bird_chirp.mp3` — notification sound

---

## Extending Nesti

### Add a new config key

1. Add `MY_KEY=default_value` to `Nesti/.env`
2. Add a property in `Helpers/AppConfig.cs`:
   ```csharp
   public static string MyKey => Str("MY_KEY", "default");
   ```
3. Use `AppConfig.MyKey` anywhere in the codebase

### Fill in the snooze payload

Open `Services/NotificationApiService.cs` → `SnoozeAsync()` and replace the body object with the real API contract fields.

### Change notification card style

Edit `Controls/NotificationControl.xaml`. Key properties:
- `CornerRadius` on the `<Border>` — card roundness
- `Background="#CCFFFFFF"` — transparency level (`CC` = 80%)
- `DropShadowEffect BlurRadius` — reduce to lower GPU memory usage

---

## Performance Notes

| Element | Memory cost | Recommendation |
|---|---|---|
| `DropShadowEffect` | High — offscreen GPU bitmap | Reduce `BlurRadius` or remove entirely |
| Animated GIFs | Medium — all frames in RAM | Replace with static PNGs for lowest usage |
| Semi-transparent backgrounds | Low | Safe to keep |

Current `BlurRadius` values have already been tuned down:
- Notification card: `20 → 12`
- Greeting bubble: `16 → 8`

---

## License

Internal / proprietary — Samsung NEST project.
