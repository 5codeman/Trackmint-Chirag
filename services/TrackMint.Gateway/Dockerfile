FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY services/TrackMint.Contracts/TrackMint.Contracts.csproj services/TrackMint.Contracts/
COPY services/TrackMint.Gateway/TrackMint.Gateway.csproj services/TrackMint.Gateway/
RUN dotnet restore services/TrackMint.Gateway/TrackMint.Gateway.csproj

COPY services/ services/
RUN dotnet publish services/TrackMint.Gateway/TrackMint.Gateway.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TrackMint.Gateway.dll"]
