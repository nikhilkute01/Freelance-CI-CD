# Base image
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 443
EXPOSE 8080
ENV PORT 8080

# Build image
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY VSRAdminAPI.csproj ./
RUN dotnet restore "VSRAdminAPI.csproj"

# Copy the rest of the source code
COPY . .

# Build and publish
RUN dotnet build "VSRAdminAPI.csproj" -c Release -o /app/build
RUN dotnet publish "VSRAdminAPI.csproj" -c Release -o /app/publish

# Final image
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# Pass PORT from Azure
ENV ASPNETCORE_URLS=http://+:$PORT
ENTRYPOINT ["dotnet", "VSRAdminAPI.dll"]
