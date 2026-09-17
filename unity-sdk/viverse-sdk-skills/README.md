# viverse-unity-sdk-skills

Independent VIVERSE Unity SDK skills repository for LLM-friendly skill discovery and loading.

## Structure
- `skills/`: skill content (SKILL.md, patterns, examples, rules)
- `catalog/`: machine-readable index and routing
- `schemas/`: JSON schemas for validation
- `bundles/`: cross-skill shared guidance
- `docs/`: authoring and consumption guides
- `scripts/`: validation/index build utilities
- `tests/`: schema/catalog/route validation tests

## Scripts
- `npm run validate` — verify every skill folder has a `SKILL.md`
- `npm run build:index` — regenerate `catalog/skills.json`

## Status
Aligned with the shared VIVERSE AI Skill authoring format (see repo root `AiSkillGuildLine.md`).
