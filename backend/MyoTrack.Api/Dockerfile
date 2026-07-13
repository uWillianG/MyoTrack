FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY MyoTrack.Domain/MyoTrack.Domain.csproj MyoTrack.Domain/
COPY MyoTrack.Infrastructure/MyoTrack.Infrastructure.csproj MyoTrack.Infrastructure/
COPY MyoTrack.Api/MyoTrack.Api.csproj MyoTrack.Api/
RUN dotnet restore MyoTrack.Api/MyoTrack.Api.csproj
COPY . .
RUN dotnet publish MyoTrack.Api/MyoTrack.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyoTrack.Api.dll"]
