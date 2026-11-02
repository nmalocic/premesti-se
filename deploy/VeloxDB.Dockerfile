FROM mcr.microsoft.com/dotnet/sdk:8.0.422 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/ src/
RUN dotnet restore src/PremestiSe.Db.Api --locked-mode
RUN dotnet build src/PremestiSe.Db.Api -c Release --no-restore -o /model
RUN mkdir /assemblies && cp /model/PremestiSe.*.dll /assemblies/
RUN dotnet restore src/PremestiSe.Api --locked-mode && dotnet publish src/PremestiSe.Api -c Release --no-restore -o /probe

FROM mcr.microsoft.com/dotnet/runtime:8.0.28-bookworm-slim AS distribution
RUN apt-get update && apt-get install -y --no-install-recommends curl unzip ca-certificates && rm -rf /var/lib/apt/lists/*
RUN curl -fL https://github.com/VeloxDB/VeloxDB/releases/download/0.4.0/vlxdb.0.4.0.zip -o /tmp/veloxdb.zip \
    && printf '31c9220c3d4202b2ba18fa15e235a074bf8bcbd72f7deaa317f6ff450ff403aa  /tmp/veloxdb.zip\n' | sha256sum -c - \
    && unzip /tmp/veloxdb.zip -d /opt && rm /tmp/veloxdb.zip

FROM mcr.microsoft.com/dotnet/aspnet:8.0.28-bookworm-slim
WORKDIR /opt/vlxdb
COPY --from=distribution /opt/vlxdb ./
COPY --from=build /assemblies /assemblies
COPY --from=build /probe /probe
COPY deploy/vlxdbcfg.json ./vlxdbcfg.json
COPY deploy/config.cluster.json ./config.cluster.json
ENTRYPOINT ["dotnet", "vlxdbsrv.dll", "--config", "/opt/vlxdb/vlxdbcfg.json", "--init-persistence", "/var/lib/veloxdb", "--update-assemblies", "/assemblies", "--interactive"]
