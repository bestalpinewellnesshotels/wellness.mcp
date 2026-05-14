# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy project files
COPY ["HotelChatbot.Api/HotelChatbot.Api.csproj", "HotelChatbot.Api/"]
COPY ["HotelChatbot.Application/HotelChatbot.Application.csproj", "HotelChatbot.Application/"]
COPY ["HotelChatbot.Domain/HotelChatbot.Domain.csproj", "HotelChatbot.Domain/"]
COPY ["HotelChatbot.Infrastructure/HotelChatbot.Infrastructure.csproj", "HotelChatbot.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "HotelChatbot.Api/HotelChatbot.Api.csproj"

# Copy all source code
COPY . .

# Build
WORKDIR "/src/HotelChatbot.Api"
RUN dotnet build "HotelChatbot.Api.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "HotelChatbot.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app

# Install dependencies for Playwright
RUN apt-get update && apt-get install -y \
    ca-certificates \
    curl \
    # Playwright dependencies
    libnss3 \
    libnspr4 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libdbus-1-3 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libgbm1 \
    libasound2 \
    libpango-1.0-0 \
    libcairo2 \
    && rm -rf /var/lib/apt/lists/*

# Copy published files
COPY --from=publish /app/publish .

# Install Playwright browsers (Chromium only for efficiency)
RUN apt-get update && apt-get install -y wget && \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright dotnet exec HotelChatbot.Api.dll playwright.ps1 install chromium --with-deps || \
    (echo "Playwright install failed, trying alternative method..." && \
     wget -q -O- https://playwright.azureedge.net/builds/chromium/1161/chromium-linux.zip | \
     busybox unzip -q -d /ms-playwright/chromium-1161 -) && \
    apt-get remove -y wget && \
    apt-get autoremove -y && \
    rm -rf /var/lib/apt/lists/*

# Set Playwright browser path
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

# Expose ports
EXPOSE 8080
EXPOSE 8081

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
  CMD curl -f http://localhost:8080/api/chat/health || exit 1

ENTRYPOINT ["dotnet", "HotelChatbot.Api.dll"]
