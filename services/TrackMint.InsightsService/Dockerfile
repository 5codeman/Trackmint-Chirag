FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY services/TrackMint.Contracts/TrackMint.Contracts.csproj services/TrackMint.Contracts/
COPY services/TrackMint.InsightsService/TrackMint.InsightsService.csproj services/TrackMint.InsightsService/
RUN dotnet restore services/TrackMint.InsightsService/TrackMint.InsightsService.csproj

COPY services/ services/
RUN dotnet publish services/TrackMint.InsightsService/TrackMint.InsightsService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TrackMint.InsightsService.dll"]
