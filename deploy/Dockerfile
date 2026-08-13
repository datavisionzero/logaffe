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

# The API layer is generated from the contract before the build runs, so the
# document has to be in this stage — at the path `npm run generate` reaches for,
# which is two levels above src/web/. It is copied before the sources because it
# changes less often than they do.
COPY docs/api/openapi.json /docs/api/openapi.json

COPY src/web/ ./
RUN npm run build -- --outDir dist --emptyOutDir

# ------------------------------------------------------------- the server ----
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# What this build calls itself. The release workflow passes the tag; a build
# started by hand keeps the default the props file gives, which says plainly
# that it is not a release.
ARG VERSION=0.0.0-dev

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
RUN dotnet publish src/Logaffe.Api/Logaffe.Api.csproj -c Release -o /app -p:Version=$VERSION

# ------------------------------------------------------------ the runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Npgsql probes for GSSAPI when it opens a connection, whatever the connection
# string says about authentication. Without the library the probe writes two
# lines straight to stderr, past the logger and its levels:
#
#   Cannot load library libgssapi_krb5.so.2
#   Error: Error loading shared library libgssapi_krb5.so.2: No such file...
#
# It then falls back and connects normally, so nothing is broken — but the word
# `Error` appears on every start of a run that is going fine, and the next
# person reading the journal during a real outage has to rule it out first. Three
# megabytes to keep the log honest. This is not Kerberos authentication being
# enabled: nothing here asks for it, and satisfying the probe does not.
#
# Ahead of the COPY on purpose, so a source change does not re-run apk.
RUN apk add --no-cache krb5-libs

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
