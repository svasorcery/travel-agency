When this command is invoked, design an ACL for an external API provider.

Steps:
1. Ask: "Which module and provider?" (e.g., "Rail module, Yandex.Rasp API")
2. Ask: "Do you have an OpenAPI spec file path, or should I work from a description?"
   - If spec file path: read the file
   - If description: ask "Describe the key endpoints we need (method, path, request/response shape)"
3. Invoke the integration-mapper agent with: module name, provider name, gathered spec/description
4. Present the four artifacts for review:
   - Provider interface
   - External DTOs
   - Adapter skeleton
   - Mapping notes (including TOS constraints)
5. On approval: write files to the correct locations:
   - modules/{name}/Travel.Modules.{Name}.Core/Providers/I{Provider}Provider.cs
   - modules/{name}/Travel.Modules.{Name}.Infrastructure/Providers/{Provider}/Dto/*.cs
   - modules/{name}/Travel.Modules.{Name}.Infrastructure/Providers/{Provider}/{Provider}Adapter.cs
