# Security Policy

## Reporting vulnerabilities

If you discover a security vulnerability, please email `developer.pulsell@gmail.com` instead of opening a public issue. Include:
- Description of the vulnerability
- Steps to reproduce
- Affected components
- Suggested fix (if any)

You'll receive a response within 7 days. Once the issue is confirmed, we'll work on a fix and coordinate a disclosure timeline.

## Scope

This is a showcase / educational project. Bring-Your-Own-Keys means most secrets stay on the user's machine. Vulnerabilities of interest:
- Code execution paths from user input
- Auth bypass in identity flows
- Improper handling of API keys / secrets in logs
- Vulnerable dependencies (auto-monitored via Renovate + GitHub security advisories)
