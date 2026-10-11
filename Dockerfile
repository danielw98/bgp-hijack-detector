# The app image: the C MRT/BGP parser and the C# application (BMP station, detector,
# incident loader, dashboard). Build context is the repo root.

# The parser, built on the runtime's own base so the glibc matches. scripts/fuzz_mrt.py
# runs its sanitizer harness in this stage.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS mrt
RUN apt-get update \
    && apt-get install -y --no-install-recommends gcc libc6-dev \
    && rm -rf /var/lib/apt/lists/*
COPY detector/mrt/mrt.c detector/mrt/mrt.h detector/mrt/build.sh /mrt/
RUN sh /mrt/build.sh

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY src/BGPWatch.Core/BGPWatch.Core.csproj src/BGPWatch.Core/
COPY src/BGPWatch.Web/BGPWatch.Web.csproj src/BGPWatch.Web/
RUN dotnet restore src/BGPWatch.Web/BGPWatch.Web.csproj
COPY src/ src/
COPY docs/lecturi-bgp.md docs/
RUN dotnet publish src/BGPWatch.Web/BGPWatch.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=mrt /mrt/build/libmrt.so .
COPY detector/schema.sql detector/config.toml /data/
COPY detector/incidents /data/incidents
EXPOSE 8080 5000
ENTRYPOINT ["dotnet", "BGPWatch.dll"]
