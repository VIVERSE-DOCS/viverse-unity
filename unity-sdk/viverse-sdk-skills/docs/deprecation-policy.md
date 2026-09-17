# Deprecation Policy

- Set `status: "deprecated"` in `skill.json` and add a `successor` field pointing to the replacement skill id.
- Keep the deprecated skill available for at least one minor cycle.
- Remove only after the successor is stable and the routes table has been updated.
