# Build baseline for native Linux release packages. Do not build these on a newer host glibc.
FROM debian:12-slim
ENV DEBIAN_FRONTEND=noninteractive DOTNET_ROOT=/usr/share/dotnet \
    RUSTUP_HOME=/usr/local/rustup CARGO_HOME=/usr/local/cargo \
    PATH=/usr/share/dotnet:/usr/local/cargo/bin:$PATH
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates curl git build-essential python3 libicu72 libssl3 zlib1g libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
RUN curl --proto '=https' --tlsv1.2 -sSf https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs \
    | sh -s -- -y --no-modify-path --profile minimal --default-toolchain stable
RUN git config --global --add safe.directory /work/mintapi
WORKDIR /work/mintapi
