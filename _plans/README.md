# `_plans/` — work in progress

One file per piece of work. A plan says **what is next and in what order**; the design docs at the
repo root say **what is true and why**. When a plan's work ships, its conclusions move into the
relevant design doc or `HANDOVER.md` and the plan is archived or deleted — a stale plan read as
current is worse than no plan.

Sits outside `Assets/`, so Unity never imports it.

## Conventions

- **Phases, not tickets.** Each phase states what blocks it and what it blocks. Dependency order is
  the point of writing it down.
- **Mark status in the file itself** as phases land — `DONE 2026-08-07` on the heading, with what
  actually shipped if it differed from the plan. The difference is usually the interesting part.
- **Record decisions taken with Rob at the top**, with the date. A plan whose premises are
  invisible cannot be re-judged when they change.
- **Nothing here overrides `GAME_DESIGN_LOCKS.md` or `CAMERA_ARCHITECTURE.md`.** A plan that needs
  a lock opened is a plan that needs an ask first.

## Current

| Plan | Covers | State |
|---|---|---|
| `TIER0_PLAN.md` | `PRODUCT_DIRECTION.md` Tier 0 — the product spine (0.1–0.6) | Phases A–F DONE; balance audit run, retune partly verified |
| `TIER1_3_CONSUMABLES.md` | Tier 1.3 — the four base/tactical consumables | **DONE 2026-08-10**, device-confirmed. Overwatch Flare deliberately held |
| `BACKLOG.md` | Asked for, not yet scheduled — one section per idea, with why | Not sequenced; pick one and give it its own plan file |
