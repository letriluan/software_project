# One image: the ASP.NET Core API serves the built React app from wwwroot.
# Same origin means no CORS and the refresh-token cookie can be SameSite=Strict.

# ---- 1. frontend ----
FROM node:22-alpine AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

# ---- 2. backend ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/global.json backend/Directory.Build.props ./
COPY backend/src/TutorDesk.Api/TutorDesk.Api.csproj src/TutorDesk.Api/
RUN dotnet restore src/TutorDesk.Api/TutorDesk.Api.csproj
COPY backend/src/ src/
RUN dotnet publish src/TutorDesk.Api/TutorDesk.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- 3. runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    TZ=Etc/UTC
COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot
# Non-root user built into the Microsoft images
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TutorDesk.Api.dll"]
