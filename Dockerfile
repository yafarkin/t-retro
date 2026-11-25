FROM node:24.15.0-bookworm-slim AS frontend
WORKDIR /src/web
RUN npm install --global npm@12.1.0
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.404-noble AS backend
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Api/Api.csproj src/Api/
COPY src/Application/Application.csproj src/Application/
COPY src/Domain/Domain.csproj src/Domain/
COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
RUN dotnet restore src/Api/Api.csproj
COPY src/ src/
RUN dotnet publish src/Api/Api.csproj --configuration Release --no-restore --output /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && install -d -o app -g app /app/data /home/app/.aspnet/DataProtection-Keys
COPY --from=backend /publish/ ./
COPY --from=frontend /src/web/dist/web/browser/ ./wwwroot/
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=3 \
    CMD ["curl", "-fsS", "--max-time", "4", "http://127.0.0.1:8080/health/ready"]
ENTRYPOINT ["dotnet", "Api.dll"]
