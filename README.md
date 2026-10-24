# Fly-Brain AI for OpenKoGaMa

<img width="1920" height="1080" alt="2026-10-24_12-15-21" src="https://github.com/user-attachments/assets/7088b6dc-ae2d-4445-af8e-ec685667043a" />


An OpenKoGaMa server plugin that introduces an NPC fly driven by an emulation behavior by the real fruit fly brain project.

## Features
* **Erratic Movements:** The fly crawls around unpredictably and sits down at times.
* **Panic State:** The NPC panics instantly when taking damage.
* **Auto-Respawn:** Automatically teleports back to its initial spawn point 5 seconds after dying.

## Prerequisites
* **OpenKoGaMa Server** installed on your machine.
* **.NET SDK** (matching your OpenKoGaMa server version, typically .NET 7.0 or 8.0 i have usied .NET 10.0)

## Build Guide

1. Clone or download this repository to your local machine.
2. Open your terminal or command prompt in the project root directory.
3. Run the following command to build the project (replace the path with your actual OpenKoGaMa server directory):

```bash
dotnet build -c Release -p:ServerRoot="C:\(\your\directory\to\openkogama-server)"
```

## Installation & Usage

1. Locate the compiled `FlyBrainAI.dll` inside your project's `bin/Release/...` output folder.
2. Copy the `.dll` file and paste it into your OpenKoGaMa server's **plugins** folder.
3. In the server console or in-game as an admin, type `!reload` to load the plugin.
4. **Spawn a fly:** In-game, type `!spawnfly` or `/spawnfly`. This will spawn an NPC fly two blocks directly above your character. Run the command multiple times to spawn more flies.

## Credits & References
* **LazyLemon** - Creator of this plugin.
* **Nightus** - Creator of OpenKoGaMa and the deadzone plugin.
* **FlyWire** - Creators of the fly's brain dataset and recompilation.
* Based on the [Fly-Brain AI Emulation Project](https://github.com/eonsystemspbc/fly-brain).
