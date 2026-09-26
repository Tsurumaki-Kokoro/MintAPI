FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

# Install Rust toolchain (edition 2024 requires >= 1.85.0; stable satisfies this)
ENV RUSTUP_HOME=/usr/local/rustup \
    CARGO_HOME=/usr/local/cargo \
    PATH=/usr/local/cargo/bin:$PATH
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs \
    | sh -s -- -y --no-modify-path --profile minimal --default-toolchain stable

WORKDIR /src
COPY ["HitCircleAPI/HitCircleAPI.csproj", "HitCircleAPI/"]
COPY ["src/Ossapi/Ossapi.csproj", "src/Ossapi/"]
COPY ["native/", "native/"]
RUN dotnet restore "HitCircleAPI/HitCircleAPI.csproj"
COPY . .
WORKDIR "/src/HitCircleAPI"
RUN dotnet build "HitCircleAPI.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "HitCircleAPI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# Install Playwright dependencies
RUN apt-get update && apt-get install -y \
    libnss3 libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 \
    libxkbcommon0 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 \
    libgbm1 libasound2 libpango-1.0-0 libcairo2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# 浏览器必须在构建期装好：运行期的 `playwright install` 已从 PlaywrightRenderer 移除，
# 而且那句 `|| true` 会把失败吞掉，让容器带着"没有浏览器"的状态启动。
# 这里刻意不加任何兜底 —— 装不上就让构建失败。
# 用包自带的 node 跑 CLI，避免依赖 pwsh（aspnet 镜像里没有）。
RUN /app/.playwright/node/linux-x64/node /app/.playwright/package/cli.js install chromium

ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "HitCircleAPI.dll"]
