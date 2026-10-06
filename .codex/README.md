# Codex project setup

Codex reads the repository's [AGENTS.md](../AGENTS.md), which contains the project layout, build/test commands, coding rules, and no-assistant-attribution policy. Claude imports that same file from `.claude/CLAUDE.md` so both agents use the same guidance.

The pre-existing project `.claude/` contained only local worktrees; there were no project skills, commands, hooks, or MCP settings to translate. Worktree checkouts are not configuration and are not copied here.

Claude's `.claude/settings.json` also disables commit, pull-request, and session-link attribution. Codex follows the shared instruction and must inspect commit messages and PR bodies before submission.

References: [Codex repository instructions](https://learn.chatgpt.com/docs/agent-configuration/agents-md), [Claude memory imports](https://code.claude.com/docs/en/memory), [Claude attribution settings](https://code.claude.com/docs/en/settings-reference#attribution).
