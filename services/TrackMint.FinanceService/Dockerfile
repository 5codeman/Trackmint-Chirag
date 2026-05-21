FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY services/TrackMint.Contracts/TrackMint.Contracts.csproj services/TrackMint.Contracts/
COPY services/TrackMint.FinanceService/TrackMint.FinanceService.csproj services/TrackMint.FinanceService/
COPY backend/ backend/
RUN dotnet restore services/TrackMint.FinanceService/TrackMint.FinanceService.csproj

COPY services/ services/
COPY backend/ backend/
RUN dotnet publish services/TrackMint.FinanceService/TrackMint.FinanceService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TrackMint.FinanceService.dll"]
