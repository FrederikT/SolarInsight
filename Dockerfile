FROM node:24.15.0-bookworm-slim AS web
WORKDIR /web
COPY src/Web/package.json src/Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS backend
WORKDIR /build
COPY global.json Directory.Build.props ./
COPY src/ ./src/
RUN dotnet restore src/Api/Api.csproj --locked-mode
COPY --from=web /web/dist/ ./src/Api/wwwroot/
RUN dotnet publish src/Api/Api.csproj --no-restore -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
RUN mkdir -p /state /keys && chown app:app /state /keys
COPY --from=backend /out/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 Solar__State=/state Solar__Keys=/keys
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]

