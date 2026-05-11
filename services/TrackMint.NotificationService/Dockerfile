FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY services/TrackMint.Contracts/TrackMint.Contracts.csproj services/TrackMint.Contracts/
COPY services/TrackMint.NotificationService/TrackMint.NotificationService.csproj services/TrackMint.NotificationService/
RUN dotnet restore services/TrackMint.NotificationService/TrackMint.NotificationService.csproj

COPY services/ services/
RUN dotnet publish services/TrackMint.NotificationService/TrackMint.NotificationService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TrackMint.NotificationService.dll"]
