FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d

ARG NODE_VERSION=24.15.0
ARG TARGETARCH
ARG DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl xz-utils ffmpeg \
    && case "$TARGETARCH" in amd64) NODE_ARCH=x64 ;; arm64) NODE_ARCH=arm64 ;; *) echo "Unsupported architecture: $TARGETARCH" >&2; exit 1 ;; esac \
    && curl -fsSL "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-${NODE_ARCH}.tar.xz" -o /tmp/node.tar.xz \
    && tar -xJf /tmp/node.tar.xz --strip-components=1 -C /usr/local \
    && rm /tmp/node.tar.xz \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

COPY src/Soso.Web/package.json src/Soso.Web/package-lock.json src/Soso.Web/
RUN npm ci --prefix src/Soso.Web \
    && npm exec --prefix src/Soso.Web -- playwright install --with-deps chromium

COPY src/Soso.Api/Soso.Api.csproj src/Soso.Api/
COPY tests/Soso.Api.Tests/Soso.Api.Tests.csproj tests/Soso.Api.Tests/
RUN dotnet restore tests/Soso.Api.Tests/Soso.Api.Tests.csproj

COPY . .

ENTRYPOINT ["bash", "tests/verify.sh"]
