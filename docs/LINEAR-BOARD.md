# Linear Board Snapshot — Armadillo

Mirror of the Linear tracking (team **Software Projects**, key `SOFTWR`). Linear is the source of truth;
this is a convenience snapshot. Captured 2026-06-18.

## Projects
| Project | Status | Linear |
|---|---|---|
| Armadillo — Core, App & CLI | In Progress | `armadillo-core-app-and-cli` |
| Armadillo — Cloud & Daily Driver | Planned | `armadillo-cloud-and-daily-driver` |
| Armadillo — Go-to-Market & Release | Planned | `armadillo-go-to-market-and-release` |

## Labels
- **GTM** — go-to-market (release, site, distribution, pricing, launch)
- **blocker** — gates progress
- **human-in-the-loop** — agent drafts/does it, a human reviews/approves
- **needs-human-action** — purely human (paid install, signup, recording, posting, repo settings)

## Issues
### Done (history)
| ID | Title |
|---|---|
| SOFTWR-42 | Phase 0–1: Spine + brain over MCP |
| SOFTWR-43 | Phase 2: Protocols — native MCP + A2A streaming + Zed ACP sidecar |
| SOFTWR-44 | Phase 3: Multi-tool adapters + learned router + parallel fan-out |
| SOFTWR-45 | Phase 4: Autonomous self-improvement (A/B promote, rollback, kill switch) |
| SOFTWR-46 | Tool catalog + registry desktop detection + per-tool asset discovery |
| SOFTWR-47 | Cross-tool chains + git-worktree isolation + Claude-hook live supervision |
| SOFTWR-48 | Phase 5: Standalone WPF dashboard + packaging (installer + CI/release) |
| SOFTWR-49 | Connect existing Claude sessions + autostart; docs |
| SOFTWR-58 | Release v0.1.0 (GTM) — published with all 3 assets |
| SOFTWR-59 | Enable GitHub Pages site (GTM) |
| SOFTWR-61 | README, LICENSE, GitHub Pages landing authored (GTM) |

### Open (roadmap)
| ID | Title | Priority | Labels |
|---|---|---|---|
| SOFTWR-53 | Wire + verify OpenAI Codex CLI as a daily second engine | **Urgent** | blocker, needs-human-action |
| SOFTWR-60 | Code signing: SignPath OSS / Azure Trusted Signing | High | GTM, needs-human-action |
| SOFTWR-50 | Verify preview tool adapters against real binaries | Medium | needs-human-action |
| SOFTWR-54 | Daily-driver hardening (connect/autostart/serve) | Medium | human-in-the-loop |
| SOFTWR-62 | Pricing & monetization design (open-core Pro + cloud) | Medium | GTM, human-in-the-loop |
| SOFTWR-63 | Launch: demo video + landing copy + posts | Medium | GTM, human-in-the-loop, needs-human-action |
| SOFTWR-51 | Cross-platform: POSIX IProcessHost / IPathProvider | Low | — |
| SOFTWR-52 | Richer learned routing (per-task-signature, cost-aware) | Low | — |
| SOFTWR-55 | Cloud: hosted/remote daemon + auth + TLS | Low | — |
| SOFTWR-56 | Cloud: A2A across machines + remote runners | Low | — |
| SOFTWR-57 | Cloud: shared team memory + learning | Low | — |
| SOFTWR-64 | Distribution: winget manifest | Low | GTM, needs-human-action (blocked by SOFTWR-60) |

## Linear documents (saved this session)
- Core: **Build Session Log**, **Architecture & Models (how/why)**, **Scenarios (every flow)**
- GTM: **Go-to-Market & Free-vs-Paid Decision**
- Cloud: **Daily-Driver Setup & Cloud Plan**
