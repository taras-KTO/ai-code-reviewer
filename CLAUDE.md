# AI Code Reviewer — Project Instructions

## Project Overview
Build a multi-agent AI system that analyzes C# code files and produces
a structured Markdown report. The system uses Claude API with multiple
specialized agents coordinated by an Orchestrator.

## Tech Stack
- .NET 10 Console Application
- Microsoft.Extensions.AI (official Microsoft AI abstractions)
- Anthropic Claude API via HTTP (no official SDK needed)
- Output: Markdown report file

## Architecture

### Agents (4 total)
1. **OrchestratorAgent** — reads input files, coordinates other agents,
   collects results
2. **SecurityAgent** — analyzes code for: hardcoded secrets, SQL injection,
   missing input validation, insecure dependencies
3. **QualityAgent** — analyzes code for: naming conventions, SOLID principles,
   code duplication, complexity
4. **ReportAgent** — combines all agent outputs into a final Markdown report

### Flow
Input (C# files path)
↓
OrchestratorAgent
↓ ↓
SecurityAgent QualityAgent ← run in PARALLEL (Task.WhenAll)
↓ ↓
ReportAgent (combines results)
↓
Output: report.md

## Project Structure
src/
Agents/
OrchestratorAgent.cs
SecurityAgent.cs
QualityAgent.cs
ReportAgent.cs
Models/
AnalysisResult.cs
AgentResponse.cs
Services/
ClaudeApiService.cs
Program.cs
appsettings.json
CLAUDE.md
.env (gitignored — stores API key)

## Implementation Rules
- Each agent has its own focused system prompt
- Agents communicate via simple model classes (no shared state)
- ClaudeApiService handles all HTTP calls to Claude API
- API key loaded from environment variable: ANTHROPIC_API_KEY
- Use Task.WhenAll for parallel agent execution
- Each agent returns AgentResponse with: AgentName, Findings (list), Summary
- ReportAgent formats everything into clean Markdown

## Agent System Prompts Style
- Security agent: strict, look for real vulnerabilities only
- Quality agent: constructive, suggest improvements with examples
- All agents: return structured output, use severity levels (HIGH/MEDIUM/LOW)

## Error Handling
- If Claude API fails: log error, continue with other agents
- If file not found: clear error message and exit
- Timeout: 30 seconds per agent call

## Entry Point Usage
```bash
dotnet run -- --path ./samples/TestCode.cs
dotnet run -- --path ./samples/  # analyze entire folder
```

## Sample Test File
Create samples/TestCode.cs with intentional issues:
- Hardcoded connection string with password
- A method longer than 50 lines
- Missing null checks
- Poor variable naming (x, y, temp)
This file is used to verify agents work correctly.

## What NOT to build
- No UI, no web server, no database
- No streaming responses (simple request/response)
- No authentication system
- Keep it simple and focused