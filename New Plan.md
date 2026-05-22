# AgentQ Current Plan

Updated: 2026-05-22

Implementation rule: finish items from top to bottom. When an item is implemented, verified, committed, and pushed, remove it from this file.

## Current Focus

AgentQ is now in beta hardening. The next work should make AgentQ's analysis answers more trustworthy by showing evidence, separating confirmed facts from assumptions, and making project analysis easier to review.

## Active Work Queue

### 1. Beta 8 release bundle

Goal:

- Package the next beta after evidence-backed analysis and UI polish are verified.

Expected behavior:

- README reflects the new beta behavior.
- Release tag is created only after build and tests pass.
- GitHub release draft contains installer, portable ZIP, and CLI package.

Primary files:

- `README.md`
- `.github/workflows/release.yml`
- `installer/AgentQ.Desktop.iss`
