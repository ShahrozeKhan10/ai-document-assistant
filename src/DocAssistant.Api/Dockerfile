# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so Docker can cache packages when only code changes.
COPY DocAssistant.Api.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ---------- run ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

# Uploaded PDFs live here (mounted as a volume). The app runs as the non-root "app" user.
RUN mkdir -p /app/storage && chown -R $APP_UID /app/storage
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DocAssistant.Api.dll"]
