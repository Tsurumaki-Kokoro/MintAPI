# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG VERSION=0.1.0
ARG SOURCE_REVISION=unknown
ARG BUILD_TIMESTAMP=unknown
ENV RUSTUP_HOME=/usr/local/rustup CARGO_HOME=/usr/local/cargo PATH=/usr/local/cargo/bin:$PATH
RUN apt-get update && apt-get install -y --no-install-recommends curl git build-essential ca-certificates \
    && rm -rf /var/lib/apt/lists/*
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs \
    | sh -s -- -y --no-modify-path --profile minimal --default-toolchain stable
WORKDIR /src
# The pinned sibling library is supplied by the release build, never resolved from a moving branch.
COPY --from=mint-osuapi . /mint-osuapi/
COPY . .
RUN test "$(uname -m)" = x86_64
# Python is used on the build host only, not in the deployed application.
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/* \
    && python3 scripts/install-preview-cli.py
RUN dotnet publish MintAPI/MintAPI.csproj -c Release -r linux-x64 --self-contained true -o /publish \
    -p:Version=$VERSION -p:SourceRevisionId=$SOURCE_REVISION -p:BuildTimestamp=$BUILD_TIMESTAMP \
    -p:PublishTrimmed=false -p:PublishSingleFile=false \
    && cp native/rosu-pp-ffi/target/release/librosu_pp_ffi.so /publish/ \
    && cp LICENSE /publish/LICENSE

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble AS final
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production PLAYWRIGHT_BROWSERS_PATH=/app/browsers
WORKDIR /app
COPY --from=build /publish/ ./
# Install the exact browser required by the restored Playwright package. No runtime downloads.
RUN /app/.playwright/node/linux-x64/node /app/.playwright/package/cli.js install --with-deps --only-shell chromium \
    && /app/MintAPI --self-test \
    && mkdir -p /data/cache /data/logs /data/tokens \
    && chown -R 10001:10001 /data \
    && rm -rf /var/lib/apt/lists/*
ENV CacheDir=/data/cache Storage__LogDirectory=/data/logs Storage__TokenCacheDirectory=/data/tokens
USER 10001:10001
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=10s --start-period=120s CMD ["/app/MintAPI", "--probe"]
ENTRYPOINT ["/app/MintAPI"]
