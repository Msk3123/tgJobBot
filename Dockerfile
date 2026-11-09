# Job Hunter bot: long polling, no inbound ports.
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
COPY src/ src/
RUN dotnet publish src/SystematiaBot.Bot/SystematiaBot.Bot.csproj -c Release -o /out --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0
# QuestPDF's native libqpdf links against zlib, which the .NET 9+ runtime images no longer ship.
# Fonts are embedded in the assembly, so no fontconfig or system fonts are needed.
RUN apt-get update \
    && apt-get install -y --no-install-recommends zlib1g \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV DOTNET_ENVIRONMENT=Production \
    TZ=Europe/Kyiv
COPY --from=build /out ./
ENTRYPOINT ["dotnet", "SystematiaBot.Bot.dll"]
