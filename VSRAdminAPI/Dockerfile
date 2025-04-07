# Base image with runtime only
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Build stage with SDK
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

# Copy only the csproj file first
COPY ["VSRAdminAPI.csproj", "."]
RUN dotnet restore "VSRAdminAPI.csproj"

# Copy everything else
COPY . .

# Build and publish
RUN dotnet build "VSRAdminAPI.csproj" -c Release -o /app/build
RUN dotnet publish "VSRAdminAPI.csproj" -c Release -o /app/publish

# Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "VSRAdminAPI.dll"]