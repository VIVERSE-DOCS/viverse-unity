# LLM Consumption Guide

## Discovery order
1. Read `catalog/skills.json` → list of all skills.
2. Match user intent against `catalog/routes.json` → resolve skill IDs.
3. For each resolved skill:
   - Read `skills/<id>/skill.json` for metadata + `read_order`.
   - Follow `read_order` (usually `SKILL.md` → `patterns/*` → `examples/*`).
   - If `dependencies` includes `bundle:*`, read the bundle first.
4. Enforce `rules.json` compliance patterns during code generation and review.

## Naming
Skill IDs are kebab-case and stable. Do not rename after publication; deprecate and add a successor instead.
