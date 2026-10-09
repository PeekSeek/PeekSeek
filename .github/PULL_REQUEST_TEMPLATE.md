<!-- The body explains why: the bug, the incident, or the constraint that made this
     necessary. Not a changelog of what moved where - the diff already says that. -->

## Why

## What changed

## Checked

- [ ] `black .` from the repository root
- [ ] `pytest -v` in `website/` (if the website or the bot changed)
- [ ] `npm run lint` in `website/tooling/` (if JS, CSS or a template changed)
- [ ] `dotnet format <sln> --verify-no-changes` and `dotnet test` (if a plugin changed)

<!-- If this touches /server/v1/*, say which plugin version it needs and whether an
     older one still works. That contract is compiled into DLLs already on servers. -->
