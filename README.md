# Enhanced Discord Rich Presence for Playnite

Bring your Playnite sessions to life, Choose exactly what shows up on Discord (game, platform, session time, progress, etc.), organize presets with priorities and conditions.

------------

## Features

##### Template mode (presets)

- Master switch Use templates to drive Presence via presets.
- Priority (top = 1) and Active toggle per preset.
- Conditions: time-of-day (incl. overnight), days of week, platforms, genres, source, plus more.
- JSON-backed storage: share, import, export.

##### Fallback mode (standard)
- If templates are off or no preset matches, your standard presence is used (custom line + toggles).

##### Assets
- Large/small images (with mapping helpers).


## Installation

1. Download the latest release (.pext) or build from source.
2. In Playnite: Add-ons → Install from file… → choose the package.
3. Restart Playnite.
4. Open Add-ons → Extensions settings → Enhanced Discord Rich Presence.

## Quick Start
(You need to create a [Discord Application](https://discord.com/developers/applications "Discord Application") in order to obtain a AppID, and also there you will be managing your game icons)

1. Create [Discord Application](https://discord.com/developers/applications "Discord Application") 
2. Playnite → Add-ons → General → Discord App ID: paste your Discord Application ID.
3. Open Discord
4. Start a game and check Discord.

**To set your game image on presence you need to generate it first: **
1. Playnite → Extensions → Discord Rich Presence → Generate Assets
2. Go to Discord App (in discord dev portal), proceed to Rich Presence > Art Assets > Add Image.
3. (optional) Create and add fallback image (shoud be named same as in plugin settings)


## Template Variables
Use these placeholders in Details format and State format (exact availability depends on your setup):

| Placeholder           | Meaning                                   |
| --------------------- | ----------------------------------------- |
| `{game}`              | Game name                                 |
| `{platform}`          | Primary platform (e.g., `PC (Windows)`)   |
| `{source}`            | Game source (e.g., `Steam`)               |
| `{genre}`             | Primary genre                             |
| `{sessionTime}`       | Current session duration (e.g., `1h 23m`) |
| `{totalPlaytime}`     | Total playtime                            |
| `{completion}`        | Completion % (if available)               |
| `{achievements}`      | Achievement progress (e.g., `12/50`)      |
| `{timeOfDay}`         | Time-of-day label                         |
| `{dayOfWeek}`         | Day of week                               |
| `{mood}` / `{phrase}` | Session mood/phrase (if enabled)          |

Tip: start with something obvious (e.g., TEMPLATE · {game}), verify it appears in Discord, then expand.

## Templates JSON
All presets are stored here:
`%AppData%\Playnite\ExtensionsData\7ad84e05-6c01-4b13-9b12-86af81775396\status_templates.json`

**Example**
```json[
  {
    "Id": "7a8e3b66-3a9b-4a8b-9c75-1d2e9f9a1001",
    "Name": "Weekend warrior",
    "Description": "Playful status on weekends",
    "DetailsFormat": "{game} — {sessionTime}",
    "StateFormat": "{platform} · {genre}",
    "IsEnabled": true,
    "Priority": 1,
    "Conditions": {
      "Platforms": ["PC"],
      "Genres": ["Action", "RPG"],
      "DaysOfWeek": ["Saturday", "Sunday"]
    }
  },
  {
    "Id": "a9e1c6d3-9e8b-4e0a-9df2-2a3b1c7f0002",
    "Name": "Night session",
    "Description": "Night owl mode",
    "DetailsFormat": "{game} — {sessionTime}",
    "StateFormat": "Night run · {platform}",
    "IsEnabled": true,
    "Priority": 2,
    "Conditions": {
      "TimeOfDay": { "StartHour": 22, "EndHour": 2 },
      "Sources": ["Steam"]
    }
  }
]
```


#### License
MIT

###### Not affiliated with Discord.
