# VIVERSE Unity Agent Skills

Skills are structured knowledge modules that package VIVERSE Unity SDK integration patterns into AI-consumable documents. They can be used by any LLM-powered assistant (Gemini, Claude, ChatGPT, Copilot, etc.) to help developers integrate VIVERSE features into their Unity projects.

## How Skills Work

Each skill folder contains:
- **SKILL.md** — Main instructions: when to use, prerequisites, step-by-step guide
- **skill.json** — Machine-readable metadata (id, version, tags, read_order, dependencies)
- **rules.json** — Optional compliance rules (regex patterns enforced by tooling)
- **patterns/** — Reusable code patterns with explanations of *why*, not just *what*
- **examples/** — Optional reference implementations and usage patterns

## Using a Skill

### With an AI assistant
Ask your AI to read the skill before starting work:
```
Read the skill at skills/viverse-unity-auth/SKILL.md
and add VIVERSE OAuth login to my Unity WebGL project.
```

## Available Skills

| Skill | Description |
|-------|-------------|
| [viverse-unity-auth](./viverse-unity-auth/) | OAuth2 login via dual-path (WebGL viverse-sdk UMD + Editor localhost HttpServer) |
| [viverse-unity-avatar](./viverse-unity-avatar/) | User profiles, avatar lists, VRM download & UniVRM10 rendering (dual-path) |
| [viverse-unity-cloudsave](./viverse-unity-cloudsave/) | Persistent player data via REST (versioned saves + key-value), dual-path |
| [viverse-unity-leaderboard](./viverse-unity-leaderboard/) | Score submission (RSA/AES encrypted) and ranking retrieval, dual-path |
| [viverse-unity-lambda](./viverse-unity-lambda/) | Invoke server-side Lambda functions via REST, dual-path |
| [viverse-unity-matchmaking](./viverse-unity-matchmaking/) | Room create/join/leave, actor management, real-time room events (dual-path) |
| [viverse-unity-multiplayer](./viverse-unity-multiplayer/) | Play SDK — data channels, 6 game modules, master/client architecture |
| [viverse-unity-game-module](./viverse-unity-game-module/) | Multiplayer game lifecycle (ready, countdown, start, end, restart) |
| [viverse-unity-network-sync](./viverse-unity-network-sync/) | Continuous per-frame position/transform sync via NetworkSyncModule |
| [viverse-unity-action-sync](./viverse-unity-action-sync/) | Broadcast discrete competitive actions (attacks, abilities, emotes) with dedupe |
| [viverse-unity-leaderboard-module](./viverse-unity-leaderboard-module/) | Real-time in-room ephemeral scores over WebRTC (not persisted) |
| [viverse-unity-general-module](./viverse-unity-general-module/) | Arbitrary freeform messages + connection/disconnection events between peers |

## Creating a New Skill

1. Create a folder under `skills/` with a kebab-case name.
2. Add a `SKILL.md` with YAML frontmatter:
```yaml
---
name: my-new-skill
description: One-line summary of what this skill enables
prerequisites: [list, of, requirements]
tags: [unity, viverse, feature-area]
---
```
3. Add `skill.json` metadata (see `schemas/skill.schema.json`).
4. Add `patterns/` and `examples/` as needed.
5. Add `rules.json` for compliance patterns if the skill has release-blocker gates.
6. Document **gotchas and edge cases** — these are the most valuable part.
7. Update this README's skill table.
8. Run `npm run build:index` and `npm run validate`.
