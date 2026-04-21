FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["global.json", "./"]
COPY ["ExiledCMS.slnx", "./"]
COPY ["src/BuildingBlocks/ExiledCms.BuildingBlocks.Hosting/ExiledCms.BuildingBlocks.Hosting.csproj", "src/BuildingBlocks/ExiledCms.BuildingBlocks.Hosting/"]
COPY ["src/BuildingBlocks/ExiledCms.BuildingBlocks.Contracts/ExiledCms.BuildingBlocks.Contracts.csproj", "src/BuildingBlocks/ExiledCms.BuildingBlocks.Contracts/"]
COPY ["src/Services/Tickets/ExiledCms.TicketsService.Api/ExiledCms.TicketsService.Api.csproj", "src/Services/Tickets/ExiledCms.TicketsService.Api/"]

RUN dotnet restore "src/Services/Tickets/ExiledCms.TicketsService.Api/ExiledCms.TicketsService.Api.csproj"

COPY . .
RUN dotnet publish "src/Services/Tickets/ExiledCms.TicketsService.Api/ExiledCms.TicketsService.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ExiledCms.TicketsService.Api.dll"]
