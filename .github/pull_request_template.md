## Summary
<!-- Provide a clear, concise overview of the package and its primary objective. -->

## Scope
- [ ] Package ID and title identified:
- [ ] Operation Mode(s) used:
- [ ] Scope strictly bounded to approved package requirements
- [ ] Unrelated refactoring explicitly excluded
- [ ] Pre-existing local work and history protected

## Verification
- [ ] Focused slice / unit tests executed (RED -> GREEN evidence where applicable)
- [ ] Candidate Full Validation executed on exact candidate HEAD
- [ ] Evidence boundaries respected (only tested behaviors claimed)
- [ ] Unverified boundaries clearly identified

## Data and Compatibility
- [ ] Real private user data was NEVER used in tests or scripts
- [ ] Persistence / storage schema impact assessed (if applicable)
- [ ] Migration and backup requirements identified (if applicable)

## Privacy and Security
- [ ] No secrets, keys, or credentials committed
- [ ] External network communications offline/mocked by default
- [ ] Logging does not expose sensitive data

## Documentation
- [ ] CHANGELOG.md updated (if applicable)
- [ ] docs/CURRENT_WORK.md updated
- [ ] docs/PROJECT_STATE.md updated (if durable facts changed)
- [ ] Relevant ADRs authored or referenced (if applicable)

## Repository Hygiene
- [ ] Only explicit file paths staged (no `git add .` or `git add -A`)
- [ ] No temporary, IDE, build, or OS artifacts committed
- [ ] No history rewriting, rebase, amend, or force-push used

## Remaining Risks & Limitations
<!-- Explicitly list any limitations, unverified edge cases, or remaining risks. -->
