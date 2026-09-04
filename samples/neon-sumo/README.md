# Neon Sumo Sample

![Neon Sumo gameplay](Neon_Sumo_Screenshot.png)

Neon Sumo is a Unity multiplayer sample project built with the VIVERSE Unity SDK.

The project demonstrates how VIVERSE authentication, matchmaking, multiplayer sessions, player synchronization, and game-state management can be integrated into a Unity WebGL experience.

Neon Sumo was developed using the VIVERSE Unity SDK AI skills as implementation guidance, making it both a working multiplayer sample and a reference for developers using the SDK and AI skills together.

## What This Sample Demonstrates

Neon Sumo includes examples of:

- VIVERSE authentication
- Multiplayer matchmaking
- Creating and joining multiplayer rooms
- Player join/leave handling
- Multiplayer player spawning
- Player identity and display names
- Networked player input and movement
- Ready-state coordination
- Match countdown and game start flow
- Multiplayer game-state synchronization
- Player elimination
- Round and match progression
- Unity WebGL deployment for VIVERSE

The project is intended as a practical reference for developers building multiplayer Unity experiences with the VIVERSE Unity SDK.

A public build is available on VIVERSE: [Neon Sumo (4 players)](https://www.viverse.com/HwW3QU2).

## Project Structure

The sample is a complete Unity project. The VIVERSE Unity SDK is already installed, so you do not need to install the SDK separately before opening the sample.

```text
samples/neon-sumo/
├── Assets/
│   └── viverse-unity-sdk/
├── Packages/
├── ProjectSettings/
└── README.md
```

## Requirements

- Unity 6000.0.49f1
- A VIVERSE Studio application
- A VIVERSE App ID

## Getting Started

1. Clone the `viverse-unity` repository.
2. Open `samples/neon-sumo` as a Unity project.
3. Allow Unity to import the project and compile scripts.
4. Create or select an application in [VIVERSE Studio](https://studio.viverse.com/).
5. Open `Assets/NeonSumo/Config/NeonSumoConfig.asset`.
6. Replace `example-app-id` with your VIVERSE App ID.
7. Open `NeonSumoMainMenuScene` and press Play.

For multiplayer testing, use two authenticated users or two separate browser/Unity instances.

The hosted VIVERSE build uses a separate App ID. The source sample remains configured with the `example-app-id` placeholder.

## VIVERSE SDK

The VIVERSE Unity SDK is included under `Assets/viverse-unity-sdk/` so the sample can be opened with little extra setup.

The standalone SDK is also in this repository under `unity-sdk/`. Use that copy when integrating VIVERSE into your own project.

## AI Skills

Neon Sumo was developed using the VIVERSE Unity SDK AI skills as implementation guidance.

The skills describe how to work with the SDK and were used during this sample’s multiplayer integration. They are maintained separately from this sample.

## Building for VIVERSE

- Use a valid VIVERSE App ID.
- Use a Release WebGL build for production.
- Avoid Development Build unless you are debugging.
- Do not log authentication tokens or sensitive session data.

## Purpose of This Sample

Neon Sumo is a reference implementation, not a production game framework.

Use it to see how the main pieces of a VIVERSE Unity multiplayer experience fit together, then adapt those patterns in your own project.

For a detailed SDK walkthrough (lobby handoff, host-authoritative sync, ActionSync events, and script map), see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
