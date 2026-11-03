FROM node:22.23.0-bookworm-slim AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0.422 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/ src/
RUN dotnet restore src/PremestiSe.Api --locked-mode
RUN dotnet publish src/PremestiSe.Api -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0.28-bookworm-slim
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "PremestiSe.Api.dll"]
