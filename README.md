# Beyond Localhost: .NET Agents in Microsoft Foundry

Demo source for the .NET Conf 2026 session **Beyond Localhost: .NET Agents in Microsoft Foundry**. See the [official session page and agenda](https://dotnet.microsoft.com/en-us/conf/2026/agenda/brk031) and the [official .NET video](https://www.youtube.com/watch?v=Dq8fcig5-Gk).

## Demos

- [01: Console agent](demos/01-console-agent/): a local console-based agent.
- [02: Host-ready agent](demos/02-host-ready-agent/): an agent prepared for Foundry hosting.
- [03: Foundry-hosted agent](demos/03-foundry-hosted-agent/): a hosted agent with tools.
- [04: Local decision agent](demos/04-local-decision-agent/): a local decision workflow. Running it makes a model call and may incur charges.
- [05: Foundry-hosted decision agent](demos/05-foundry-hosted-decision-agent/): a hosted support-routing decision agent.
- [10: Foundry-hosted Kimi agent](demos/10-foundry-hosted-kimi-agent/): a hosted Kimi tool-calling agent.

The snapshot includes the shared sources, project files, deployment manifests, and package locks required by these demos. Use the .NET SDK version pinned in `demos/global.json`. Some hosted-agent projects intentionally retain their existing .NET 10 target for the Foundry hosted runtime; local demos target .NET 11. Azure-hosted demos require the relevant Azure CLI tools and access to a configured Microsoft Foundry project. `demos/Set-DemoEnvironment.ps1` is provided for local demo settings; do not commit local settings or credentials.

## Slides

The final session-slides PDF is forthcoming and is not included; it has not been approved.

## License

Original demo code and documentation authored by El Bruno outside Demo 05 are licensed under the root MIT license. Demo 05 retains its existing MIT license and complete third-party notices. The root license does not replace or relicense that material; other third-party components remain under their respective terms.

This is a fresh file snapshot with no history from the private source repository.