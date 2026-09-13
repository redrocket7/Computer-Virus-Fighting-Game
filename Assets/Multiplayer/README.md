# Multiplayer Prototype (LAN)

First milestone: **Host / Join lobby** and **two networked players moving** in an empty arena.

## Packages (important for Unity 6000.5)

This project embeds:
- `Packages/com.unity.netcode.gameobjects` (2.13.2)
- `Packages/com.unity.transport` (2.6.0)

Unity 6000.5 ships a builtin Transport 6.5 that conflicts with NGO’s Transport 2.6 dependency, so these are local `file:` packages instead of registry installs.

## Setup (once)

1. Let Unity finish importing packages (no Netcode missing errors).
2. Run **Tools → Virus Game → Setup Multiplayer Scaffold**
3. Confirm these exist:
   - `Assets/Multiplayer/Scenes/MultiplayerLobby.unity`
   - `Assets/Multiplayer/Scenes/MultiplayerArena.unity`
   - `Assets/Multiplayer/Prefabs/NetworkPlayer.prefab`

## How to test

1. Open **MultiplayerLobby** → Play → **Host**
2. **Build And Run** a second instance → **Join** `127.0.0.1` port `7777`
3. Move with **WASD / arrows**

From Gameplay: press **F8** or use the **MULTIPLAYER** button.
