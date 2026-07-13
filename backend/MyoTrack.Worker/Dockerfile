FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY MyoTrack.Domain/MyoTrack.Domain.csproj MyoTrack.Domain/
COPY MyoTrack.Infrastructure/MyoTrack.Infrastructure.csproj MyoTrack.Infrastructure/
COPY MyoTrack.Worker/MyoTrack.Worker.csproj MyoTrack.Worker/
RUN dotnet restore MyoTrack.Worker/MyoTrack.Worker.csproj
COPY . .
RUN dotnet publish MyoTrack.Worker/MyoTrack.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MyoTrack.Worker.dll"]
