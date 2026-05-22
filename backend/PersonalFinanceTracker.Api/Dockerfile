FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/ backend/
COPY services/TrackMint.Contracts/ services/TrackMint.Contracts/
RUN dotnet restore backend/PersonalFinanceTracker.Api/PersonalFinanceTracker.Api.csproj
RUN dotnet publish backend/PersonalFinanceTracker.Api/PersonalFinanceTracker.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PersonalFinanceTracker.Api.dll"]
