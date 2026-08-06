# The two toolchains ADR 0001 accepted meet here and nowhere else: the SPA is
# built by Node, the server by the .NET SDK, and the runtime image carries
# neither of them.
#
# Build context is the repository root:
#   docker build -f deploy/Dockerfile -t logaffe .

# ---------------------------------------------------------------- the SPA ----
FROM node:24-alpine AS web
WORKDIR /web

COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci

COPY src/web/ ./
RUN npm run build -- --outDir dist --emptyOutDir

# ------------------------------------------------------------- the server ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore against the manifests alone, so that a change to a source file does
# not invalidate the package layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Logaffe.Domain/Logaffe.Domain.csproj src/Logaffe.Domain/
COPY src/Logaffe.Application/Logaffe.Application.csproj src/Logaffe.Application/
COPY src/Logaffe.Infrastructure/Logaffe.Infrastructure.csproj src/Logaffe.Infrastructure/
COPY src/Logaffe.Api/Logaffe.Api.csproj src/Logaffe.Api/
RUN dotnet restore src/Logaffe.Api/Logaffe.Api.csproj

COPY src/Logaffe.Domain/ src/Logaffe.Domain/
COPY src/Logaffe.Application/ src/Logaffe.Application/
COPY src/Logaffe.Infrastructure/ src/Logaffe.Infrastructure/
COPY src/Logaffe.Api/ src/Logaffe.Api/
RUN dotnet publish src/Logaffe.Api/Logaffe.Api.csproj -c Release -o /app

# ------------------------------------------------------------ the runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot

# Everything that is not in the database lives here, and it is a mount rather
# than part of the image: a container can be destroyed and recreated without
# losing anything.
RUN mkdir -p /var/lib/logaffe && chown -R $APP_UID:$APP_UID /var/lib/logaffe /app
VOLUME /var/lib/logaffe

# `docker compose exec logaffe logaffe backup` has to find the binary by name.
ENV PATH="/app:${PATH}" \
    ASPNETCORE_HTTP_PORTS=8080 \
    Logaffe__VolumePath=/var/lib/logaffe

EXPOSE 8080

# It is reachable from the open internet, so it does not run as root.
USER $APP_UID

ENTRYPOINT ["logaffe"]
