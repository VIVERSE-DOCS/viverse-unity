# VIVERSE Unity SDK

For a Unity game, use this repository's C# SDK. Do not use the VIVERSE JavaScript SDK or create a `.jslib` for normal authentication, cloud save, multiplayer, leaderboard, profile, or avatar integration.

The namespace is `ViverseSDK`. Requires Unity 2021.2 or newer.

## Which API to call

* **Current:** `AuthManager`. **Legacy:** `LoginManager`. Do not use `LoginManager` for a new project.
* **Current:** `CloudSaveClient`. **Legacy:** `CloudSaveService`. Do not use `CloudSaveService` for a new project.
* **Current:** `LeaderboardClient`. **Legacy:** `LeaderboardService`. Do not use `LeaderboardService` for a new project.
* `SDK_v0.96.unitypackage` is legacy. Import `unity-sdk/Viverse-Unity-SDK-1.2.0.unitypackage`.

Other current clients are `MultiplayerClient` and `AvatarClient`.

## Where to read more

* Public docs: [VIVERSE Unity SDK](https://docs.viverse.com/developer-tools/unity/viverse-unity-sdk)
* API guide in this repo: [unity-sdk/README.md](unity-sdk/README.md)
