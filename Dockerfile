# Systematia Assistant bot: long polling, no inbound ports.
# Runtime state (SQLite, logs, exports) lives in /app/data, /app/logs, /app/exports — mount them as volumes.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props SystematiaBot.sln ./
COPY src/SystematiaBot.Core/SystematiaBot.Core.csproj src/SystematiaBot.Core/
COPY src/SystematiaBot.Infrastructure/SystematiaBot.Infrastructure.csproj src/SystematiaBot.Infrastructure/
COPY src/SystematiaBot.Bot/SystematiaBot.Bot.csproj src/SystematiaBot.Bot/
RUN dotnet restore src/SystematiaBot.Bot/SystematiaBot.Bot.csproj
COPY config/ config/
COPY prompts/ prompts/
COPY demo/ demo/
COPY src/ src/
RUN dotnet publish src/SystematiaBot.Bot/SystematiaBot.Bot.csproj -c Release -o /out --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
ENV DOTNET_ENVIRONMENT=Production \
    TZ=Europe/Kyiv
COPY --from=build /out ./
ENTRYPOINT ["dotnet", "SystematiaBot.Bot.dll"]
