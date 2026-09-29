# LEARNING NOTE — a Dockerfile is a recipe for building and running the app.
# Stage 1 uses the big SDK image to compile; stage 2 copies only the output
# into a small runtime image. The final image never contains your source code.

# ---- Stage 1: build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY OrderApi.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ---- Stage 2: run ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderApi.dll"]
