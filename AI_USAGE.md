# AI Usage & Mistake Log

This document records the AI-assisted development of the School Admission System, including tools used, issues encountered, and how they were caught and resolved.

---

## 1. AI Assistance Overview
- **Models / Tools:** Claude Opus & Gemini 3.8 Flash via Antigravity Agentic Assistant.
- **Scope:** Architecture formulation, ASP.NET Core minimal APIs and raw SQL concurrency queries, EF Core migrations, React + Vite TypeScript components, Docker Compose configuration, and automated test fixtures.

---

## 2. Mistake & Resolution Log

The following incidents occurred during implementation and were caught and corrected:

1. **Missing DOTNET_ROOT in Shell Environment for Tool Invocation**
   - *Issue:* Running `dotnet-ef migrations add` failed with `libhostfxr.so not found` because `dotnet-ef` required `DOTNET_ROOT` to locate the runtime in user directories (`~/.dotnet`).
   - *Fix:* Exported `DOTNET_ROOT="$HOME/.dotnet"` and updated `PATH` to point to the local SDK binaries before invoking EF CLI.

2. **Missing Project Reference in Test Suite**
   - *Issue:* Compilation of `SchoolAdmission.Tests` failed with `CS0234: The type or namespace name 'Api' does not exist in the namespace 'SchoolAdmission'`.
   - *Fix:* Added project reference `<ProjectReference Include="..\..\src\SchoolAdmission.Api\SchoolAdmission.Api.csproj" />` to the test project.

3. **Vite TypeScript Strict Unused Variable Check**
   - *Issue:* Frontend build failed with `TS6133: 'useEffect' is declared but its value is never read` in `App.tsx` and unused `React` import in `ErrorBoundary.tsx` due to TypeScript compiler settings (`noUnusedLocals: true`).
   - *Fix:* Removed the unused `useEffect` and `React` imports from the frontend components and verified with a clean `npm run build`.

4. **Vite `import.meta.env` Type Resolution in Strict Bundler Mode**
   - *Issue:* `tsc` reported `TS2339: Property 'env' does not exist on type 'ImportMeta'`.
   - *Fix:* Safely typed access as `((import.meta as any).env?.VITE_API_URL)` to satisfy TypeScript compilation while retaining dynamic runtime fallback to `window.__APP_CONFIG__.API_URL`.

5. **Docker Build Host Object Leak**
   - *Issue:* Docker build failed with `NETSDK1064: Package Microsoft.CodeAnalysis.Analyzers was not found` because local host `bin/` and `obj/` were copied into Linux container layers.
   - *Fix:* Updated `.dockerignore` to recursively match `**/bin/` and `**/obj/`.

6. **Static Container Name Preventing Docker Compose Scaling**
   - *Issue:* Running `docker compose up --scale backend=2` produced warnings due to a fixed `container_name: school_admission_backend`.
   - *Fix:* Removed static container name on the scalable service so Docker Compose can generate distinct container names (`school-admission-system-backend-1`, `backend-2`).

7. **Transitive Vulnerability Warnings in Test Suite**
   - *Issue:* Vulnerability scan flagged transitive `System.Net.Http 4.3.0` via legacy `xunit 2.5.3`.
   - *Fix:* Upgraded `xunit` to 2.9.2 and `coverlet.collector` to 6.0.2, achieving 0 vulnerable packages.
