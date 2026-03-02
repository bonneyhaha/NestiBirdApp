# Nesti Bird Assistant

A lightweight Windows desktop notification assistant built with **WPF / C# / .NET 8**. Nesti lives in the bottom-right corner of your screen as a transparent, always-on-top overlay. It connects to a WebSocket server to receive real-time notifications, which it displays as animated cards with sound.

---

## Features

- **Transparent overlay** — no taskbar entry, no title bar, fully click-through on empty areas
- **Animated bird mascot** — sits in the corner, GIF rendered via custom zero-allocation player
- **Real-time notifications** — WebSocket with heartbeat, jitter, 15s timeout, and exponential back-off reconnection
- **Per-user WebSocket channel** — reads Windows login name, calls a REST API to get `mEmpID`, connects to `{WS_URL}/{mEmpID}`
- **Notification cards** — slide in from the right with smooth animations
- **Snooze** — calls a configurable MARS API to snooze a notification
- **Auto-dismiss** — when timer expires, calls mark-as-read API with `actionTaken: "Automatic Read"`
- **Dismiss** — close button calls mark-as-read API with `actionTaken: "Manual Read"`
- **Sound** — plays a bird chirp on every new notification (MP3, toggleable)
- **Hide / Close controls** — appear on bird hover; hide until next notification or exit entirely
- **Dummy test mode** — fires rotating fake notifications on a timer, no server needed
- **Zero-recompile config** — all URLs, timeouts, and flags live in a `.env` file
- **Low RAM footprint** — software rendering, system DPI manifest, no DropShadowEffect, frozen brushes, shared GIF WriteableBitmap

---

## Screenshots / Layout

```
┌─────────────────────────────────────────────────────────────────┐
│                                              (transparent)       │
│                                                                   │
│                                  ┌───────────────────────────┐   │
│                                  │ 🔔 Notification Title   ⏰ ✕ │
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
| Animated GIFs | Custom `SharedGifPlayer` — shared `WriteableBitmap`, zero frame cache |
| Config parsing | [DotNetEnv](https://github.com/motdotla/dotnet-env) 3.1.1 |
| WebSocket | `System.Net.WebSockets.ClientWebSocket` |
| Audio | `System.Windows.Media.MediaPlayer` (MP3) |

---

## Project Structure

```
Nesti/                              ← Solution root
├── README.md                       ← This file
├── Nesti.slnx
└── Nesti/                          ← C# project
    ├── .env                        ← All runtime configuration (edit freely, no recompile)
    ├── Nesti.csproj
    ├── app.manifest                ← System DPI awareness (not PerMonitorV2)
    ├── App.xaml / App.xaml.cs      ← Forces software-only rendering before startup
    ├── MainWindow.xaml             ← Transparent overlay window
    ├── MainWindow.xaml.cs          ← Orchestration, click-through, WebSocket wiring
    ├── assets/
    │   ├── nest_bird.gif               ← Bird mascot animation
    │   ├── jarvis.gif                  ← Notification card avatar animation
    │   ├── cross_button_notif_icon.png ← Card dismiss button
    │   └── bird_chirp.mp3              ← Notification sound
    ├── Models/
    │   └── NotificationMessage.cs      ← JSON payload model + DedupeKey logic
    ├── Helpers/
    │   ├── AppConfig.cs                ← Reads .env into typed static properties
    │   ├── UserHelper.cs               ← Windows username, full-name API, greeting text
    │   └── SharedGifPlayer.cs          ← Custom GIF compositor (WriteableBitmap, ArrayPool)
    ├── Services/
    │   ├── IWebSocketSource.cs         ← Interface — real and dummy share the same contract
    │   ├── WebSocketService.cs         ← Real WebSocket: URL resolution, reconnect, heartbeat
    │   ├── DummyWebSocketService.cs    ← Test mode: fires fake notifications on a timer
    │   ├── SoundService.cs             ← MP3 playback via MediaPlayer
    │   └── NotificationApiService.cs   ← Snooze + mark-as-read HTTP calls + local cache
    ├── Controls/
    │   ├── NotificationControl.xaml    ← Notification card UI (SVG snooze icon, shadow border)
    │   └── NotificationControl.xaml.cs ← Card animations, button handlers, cleanup
    ├── guide.txt                   ← Full file-by-file reference guide
    ├── websocket.txt               ← WebSocket connection flow documentation
    ├── websocket_req.txt           ← JS vs C# WebSocket implementation comparison
    ├── api.txt                     ← REST API calls documentation
    └── explanation.txt             ← Why WPF uses more RAM than 15 MB
```

---

## Quick Start

### Prerequisites

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Run in development

```bash
cd "POC-2-WPF/Nesti/Nesti"
dotnet restore
dotnet build --configuration Debug
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
| `WS_URL` | `wss://localhost:8080/ws` | Base WebSocket URL — final URL is `{WS_URL}/{mEmpID}` |
| `WS_RECONNECT_BASE_MS` | `2000` | Initial reconnect delay (ms); doubles each attempt + ±10% jitter |
| `WS_RECONNECT_MAX_ATTEMPTS` | `10` | Max reconnect attempts before giving up |
| `API_BASE_URL` | *(blank)* | Full endpoint URL called as `GET {API_BASE_URL}?CorpID=Corp\{username}` to retrieve `mEmpID` |
| `API_GET_FULLNAME_PATH` | *(blank)* | Path appended to `API_BASE_URL` to resolve display name |
| `BIRD_DEFAULT_URL` | *(blank)* | URL opened when bird is clicked |
| `NOTIFICATION_DURATION_MS` | `10000` | How long notification cards stay on screen (ms) |
| `MAX_NOTIFICATIONS` | `5` | Max cards visible simultaneously |
| `SOUND_ENABLED` | `true` | Enable / disable chirp sound |
| `USE_REAL_WEBSOCKET` | `true` | `false` = dummy test mode |
| `DUMMY_INTERVAL_MS` | `5000` | Fake notification interval in test mode (ms) |
| `MARS_SNOOZE_URL` | *(blank)* | POST endpoint for snooze action |
| `MARK_AS_READ_URL` | *(blank)* | POST endpoint for mark-as-read (auto, manual, card click) |
| `SNOOZE_DURATION_MINUTES` | `5` | Duration sent in snooze payload |

---

## WebSocket Connection Flow

```
1. Read Windows login name     Environment.UserName → "jdoe"
2. Build Corp ID               "Corp\" + "jdoe" → "Corp\jdoe"
3. Call REST API               GET {API_BASE_URL}?CorpID=Corp%5Cjdoe
4. Parse mEmpID from response  { "mEmpID": "12345", ... }  → "12345"
5. Build WebSocket URL         {WS_URL}/12345
6. Connect with 15s timeout
7. Heartbeat every 25s         → sends {"type":"ping"}, expects {"type":"pong"} within 7s
8. On disconnect               → exponential back-off reconnect (base × 2^attempt ± 10% jitter)
```

If `API_BASE_URL` is empty, skips steps 2–5 and connects to `WS_URL` directly.

---

## WebSocket Message Format

Nesti expects JSON text frames in this shape:

```json
{
  "instanceId":       "421c6789-fb18-4774-a880-3aa4f2435c2b",
  "title":            "Greetings!",
  "message":          "Good evening, Hope you had a great tea break.",
  "notificationType": "Broadcast",
  "priority":         1,
  "priorityText":     "High",
  "createdAt":        "2025-11-17T19:48:37.32",
  "expiryDate":       "2025-11-18T18:58:33.46",
  "isRead":           false,
  "isVisible":        false,
  "isDismissed":      false,
  "user_id":          -1,
  "displayDuration":  5,
  "type":             "notification",
  "url":              "http://your-app-url/hub/"
}
```

**Fields used by Nesti:**
- `instanceId` — deduplication key + passed as `instance_id` in all API payloads
- `user_id` — if `-1`, all API calls (mark-as-read, snooze) are skipped; card just slides out
- `title`, `message` — displayed on the card
- `url` — opened in browser when card body is clicked
- `type` — if `"pong"`, treated as heartbeat response (not shown as a notification)

Deduplication: `instanceId ?? id ?? "{title}|{message}|{timestamp}"`. Duplicates are silently dropped.

Heartbeat frames (handled internally, never surfaced as notifications):
```json
{ "type": "ping", "timestamp": "..." }   ← sent by Nesti every 25s
{ "type": "pong", "timestamp": "..." }   ← expected from server within 7s
```

---

## Notification Card Actions

All API calls are **skipped** when `user_id == -1` (broadcast) or `USE_REAL_WEBSOCKET=false`. The card always slides out regardless.

| Trigger | API | Payload |
|---|---|---|
| **Timer expires** (auto) | `POST MARK_AS_READ_URL` | `instance_id`, `userSession` (mEmpID), `isClicked: true`, `actionTaken: "Automatic Read"` |
| **Dismiss button** or **card click** | `POST MARK_AS_READ_URL` | `instance_id`, `userSession` (mEmpID), `isClicked: true`, `actionTaken: "Manual Read"` |
| **Snooze button** | `POST MARS_SNOOZE_URL` | `instance_id`, `userSession` (mEmpID), `snoozeDurationMinutes` (from `SNOOZE_DURATION_MINUTES`) |

---

## Bird Controls

| Button | What it does |
|---|---|
| **Hide** `[–]` | Hides bird + notification panel. Restores automatically on next notification |
| **Close** `[✕]` | Exits Nesti completely |

Both buttons are revealed only on bird hover and collapse after 1.2 s of inactivity.

---

## RAM Optimizations

| Optimization | What it saves |
|---|---|
| `RenderMode.SoftwareOnly` | Prevents D3D device + VRAM allocations entirely |
| System DPI manifest (not PerMonitorV2) | Fixed render-target size — no reallocation on monitor moves |
| `AllowsTransparency="True"` + `Background="Transparent"` | WS_EX_LAYERED with per-pixel alpha — transparent window without extra surfaces |
| DropShadowEffect removed | Saves ~20–30 MB for 5 simultaneous cards at 200% DPI |
| Shadow via offset Border | Zero additional render targets |
| `po:Freeze="True"` on all brushes | Skips change-notification overhead; shared across card instances |
| `SharedGifPlayer` + `WriteableBitmap` | One GIF decode per URI — O(1) memory regardless of card count |
| `ArrayPool<byte>` in GIF player + WebSocket | Buffer reuse — no repeated heap allocations per frame/message |

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
> - `.env` — configuration file (edit to point at your server)
> - `assets/bird_chirp.mp3` — notification sound

---

## Extending Nesti

### Add a new config key

1. Add `MY_KEY=default_value` to `Nesti/.env`
2. Add a property in `Helpers/AppConfig.cs`:
   ```csharp
   public static string MyKey => Str("MY_KEY", "default");
   ```
3. Use `AppConfig.MyKey` anywhere

### Change snooze duration

Set `SNOOZE_DURATION_MINUTES=15` (or any value) in `.env`. Default is `5`.

### Change notification card style

Edit `Controls/NotificationControl.xaml`. Key properties:
- `CornerRadius` on the card `<Border>` — card roundness
- Shadow `<Border Margin="2,3,0,0">` opacity — shadow strength
- `FontSize` on `TitleBlock` / `MessageBlock` — text size

### Swap the bird GIF

Replace `assets/nest_bird.gif` and rebuild. The `SharedGifPlayer` handles any valid GIF automatically.

---

## License

Internal / proprietary — Samsung NEST project.
