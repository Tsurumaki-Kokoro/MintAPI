# MintOsuApi contract validation

Opt-in public read/compute runner. It does not start MintAPI, MySQL, or Redis and is not part of the normal test suite. Credentials come from the ignored `MintAPI/appsettings.Development.json` (`OsuApi` section).

From the repository root, capture current responses:

```sh
dotnet run --project tools/MintOsuApi.ContractValidation -- "$PWD" tools/MintOsuApi.ContractValidation/cases.json MintAPI/cache/mintosuapi-validation/new-run
```

Replay a completed capture without credentials or network:

```sh
dotnet run --project tools/MintOsuApi.ContractValidation -- "$PWD" tools/MintOsuApi.ContractValidation/cases.json MintAPI/cache/mintosuapi-validation/validated --replay
```

`validated` was collected locally during the 2026-10-04 audit and is ignored by Git; other checkouts must first capture their own responses and replay that directory. Old public IDs may become unavailable. The committed manifest contains 115 concrete cases for 46 methods; it can be reduced to a small batch before making real requests. `devdocs/mintosuapi-validation-inputs.json` records the user-provided seeds; the manifest also contains IDs discovered from list responses.

Each case has a unique `name`, a client `method`, an `arguments` object using C# parameter names, and optional `resultPath` for methods which unwrap a response array. Enum arguments accept C# names; modes use API names (`osu`, `taiko`, `fruits`, `mania`), mods accept legacy acronyms. Missing parameters use method defaults.

The runner serially captures the original HTTP status/body, request path/body, response version, time, effective redirect URL, and body SHA-256 before model conversion. Authorization headers and secrets are excluded. Raw bodies and token caches stay in the ignored output directory. Requests are at least 1.1 seconds apart; a 429 stops the batch and records Retry-After rather than continuing requests. There are no automatic retries. Read-only difficulty calculation is the sole allowed POST; chat/forum writes and user-authenticated reads are blocked.

Replay verifies request identity and compares raw JSON with the typed result from exactly that captured body. It checks nested keys, arrays, numbers, nulls, dates and values. Intentional rank-status and legacy-mod representations are normalized. Match mods settings are retained in `RawMods` and current score fields in `ModernScore`. Added default model fields are not considered server guarantees. The exit code is nonzero on failures or remaining differences; empty arrays can have zero differences and still do not prove item-field coverage.

This is observed-response verification, not a full OpenAPI schema validator. Undocumented metadata explicitly kept as JObject/JToken is retained without imposing a guessed schema. Additional nullable/conditional payloads, grants, unavailable rooms, invalid inputs, and optional filter combinations must remain separate coverage items. Representative public payloads are reduced into `../mint-osuapi/tests/MintOsuApi.Tests/Fixtures/contract_live_samples.json`; synthetic overflow/null/error cases are separately identified in tests.

See `devdocs/mintosuapi-validation-results.md` for findings, compatibility changes, exclusions, and field/pagination checks.

For the complete supplied sample capture, verify ID relationships, Mod effects and pagination as well:

```sh
python3 tools/MintOsuApi.ContractValidation/check_semantics.py MintAPI/cache/mintosuapi-validation/validated
```

The semantic checker is intentionally tied to the recorded UID and named cases. Adapt its expected identities alongside the manifest for another sample set. Some payloads and assertions are time dependent; a failed future run should be investigated, not mechanically rebaselined.
