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

# Install ca-certificates for HTTPS
RUN apt-get update && apt-get install -y ca-certificates curl && rm -rf /var/lib/apt/lists/*

# Copy published files
COPY --from=publish /app/publish .

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
