When this command is invoked, create an ADR for a recent or specified architectural decision.

Steps:
1. If the user provided a topic: go to step 3
2. Run: git diff HEAD~5 --name-only — identify what architectural change was made; propose a topic
3. Confirm the topic with the user in one sentence: "I'll write an ADR about X — does that sound right?"
4. Invoke the adr-writer agent with the confirmed topic
5. Present the drafted ADR to the user
6. On approval: write the file to docs/adr/NNNN-kebab-title.md and commit:
   git add docs/adr/NNNN-*.md && git commit -m "docs: add ADR NNNN - {title}"
