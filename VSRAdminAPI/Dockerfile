# Base image with runtime only
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
USER root
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Build stage with SDK
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy only the .csproj first
COPY ["VSRAdminAPI.csproj", "./"]
RUN dotnet restore "VSRAdminAPI.csproj"

# Copy rest of the code
COPY . ./

# ✅ Build from /src where the .csproj is located
RUN dotnet build "VSRAdminAPI.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "VSRAdminAPI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "VSRAdminAPI.dll"]
