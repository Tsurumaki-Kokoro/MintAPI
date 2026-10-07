# Repository Guidelines

## Project Structure & Module Organization

`MintAPI/` is the ASP.NET Core service: controllers expose endpoints, services handle osu! access and rendering, `Data/` holds EF Core persistence, and `Middleware/` contains request handling. HTML templates live under `MintAPI/Rendering/`; images and fonts live in `MintAPI/wwwroot/`. `../mint-osuapi/src/MintOsuApi/` is the reusable osu! API client (namespace `MintOsuApi`, entry points `OsuClient` and `OsuV1Client`). `native/rosu-pp-ffi/` contains the Rust performance calculator and its C# binding. The matching xUnit projects are `tests/MintAPI.Tests/` and `../mint-osuapi/tests/MintOsuApi.Tests/`; JSON samples are in `../mint-osuapi/tests/MintOsuApi.Tests/Fixtures/`.

## Build, Test, and Development Commands

- `dotnet build MintAPI.sln` builds all .NET projects; building the API also runs `cargo build --release` for the native library. Install the .NET 9 SDK and Rust toolchain first.
- `dotnet test MintAPI.sln` runs both xUnit suites.
- `dotnet run --project MintAPI --launch-profile http` starts the development API on `http://localhost:5251`. Supply local configuration and reachable MySQL and Redis services first. Development OpenAPI and Scalar pages are enabled by this profile.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, file-scoped namespaces, PascalCase types and public members, camelCase locals, and `_camelCase` private fields. Nullable reference types and implicit usings are enabled. Keep interfaces beside their service implementations and use dependency injection through `Program.cs`. Add XML comments to controller actions and parameters when changing endpoints; those comments feed the OpenAPI document. No repository-wide formatter or lint configuration is checked in, so match nearby code and keep diffs focused.

## Testing Guidelines

Use xUnit `[Fact]` or `[Theory]`; place tests in `Unit/` or `Integration/` within the relevant test project. Name test classes after the subject, such as `ApiKeyMiddlewareTests`, and methods after the behavior, such as `Constructor_throws_when_ApiKey_is_empty`. The Playwright rendering integration tests need Chromium installed through Playwright. The projects include Coverlet, but no coverage threshold is configured.

## Commit & Pull Request Guidelines

Recent commits use a type prefix and emoji marker, for example `feat: :memo: 控制器注释接入 OpenAPI 文档`. Keep commit subjects specific to the change. In pull requests, describe behavior and configuration changes, link related issues when applicable, and report the build and test results. Include example API output or rendered images when those outputs change.

## Configuration & Secrets

Copy `MintAPI/appsettings.Development.json.example` to the ignored `appsettings.Development.json` and fill in `ApiKey`, osu! credentials, and connection strings. Do not commit secrets, `cache/`, or `logs/`. Production settings can use environment variables such as `OsuApi__ClientSecret` and `ConnectionStrings__DefaultConnection`.

## API Error and Logging Contract

- New and changed controller error branches must use `ApiErrors.Result(ErrorCatalog.X)`. Do not return raw error strings, anonymous error objects, or duplicate status/code/message definitions. Add a named `ErrorDefinition` to `MintAPI/Errors/ErrorCatalog.cs` when a distinct business result is needed; document it in `docs/api-errors.md` and test it. Error codes are client contracts: wording may change, but existing code meanings and HTTP status must remain stable unless a compatibility change is explicitly approved.
- Distinguish unbound users, missing resources, empty official scores, uncollected local scores, empty filtered results/pages, upstream failures, and rendering failures. Empty local history does not prove the player has never played. Never turn network/service failures into "no record" or parameter errors. Translate an upstream 404 at the resource lookup boundary using `ApiLookupException` when needed.
- Controller errors return `application/problem+json` with `status`, `code`, `message`, and `traceId`, even for image endpoints and `Accept: image/png`. Keep successful media responses unchanged, and include the error media type and relevant status codes in endpoint OpenAPI/XML documentation. The legacy text classifier exists only for compatibility; new code must not depend on it.
- Let the global `ApiErrorFilter` and request logging scope handle response serialization and correlation. Use `diagnostic:` only for internal reasons; never put exception messages, paths, credentials, tokens, or upstream response bodies into player-facing messages. If catching an exception, log the exception object with useful resource IDs before returning a factory result; otherwise let the global exception handling record it. Do not swallow retryable exceptions or request cancellation.
- Expected lookup/validation errors use Information; service failures use Error with the original reason and exception stack when available; retryable resource pressure uses Warning and `Retry-After`. Add request context only through the allowlist in `ApiErrors.Context`; do not log full headers, authentication tokens, or arbitrary query/body payloads.
- Reuse `ApiErrorAssertions.AssertAsync` for new endpoint HTTP tests. Exercise the relevant business-error branches and at least one dependency failure. Keep the common `ApiErrorContractTests` passing: image-only Accept negotiation, automatic model validation, explicit 500, unhandled exception, upstream failure, trace/log correlation, diagnostic isolation, and 503 retry headers. A controller method test alone does not verify HTTP formatting or prevent a 406 regression.

## Rendering Template Design

- Establish a clear information hierarchy: player/map identity, the primary result, supporting statistics, then secondary metadata. Preserve existing information and data meaning when redesigning; use secondary areas rather than deleting fields to simplify a layout.
- Prefer restrained typography, whitespace, alignment, tables, and dividers. Avoid stacks of rounded cards, decorative gradients, and explanatory implementation notes inside data areas. Use accent color sparingly for meaningful emphasis or status, and keep text readable against backgrounds.
- Follow the surrounding theme's fonts, spacing, colors, icons, and attribution conventions. Keep layout/presentation in the template and theme model; do not embed API calls or business decisions in HTML. Escape all external text before HTML insertion.
- Design for long multilingual names, long difficulty titles, large numbers, missing fields, empty results, and variable row counts. Use explicit wrapping/truncation rules, consistent numeric precision and units, and stable alignment. Missing data must remain visibly unknown rather than being fabricated as zero.
- Match the intended exported image dimensions and actual bot viewing size. Avoid clipped text, horizontal overflow, overlapping elements, and unreadably small secondary text; use pagination or increased height when density requires it.
- Verify template changes through the real rendering pipeline with representative and boundary-case samples, then open and inspect the generated images. HTML assertions or a successful build alone do not constitute visual acceptance. Preserve the existing MintAPI attribution and rendering date.
