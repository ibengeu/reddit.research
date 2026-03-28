FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY src/RedditCrawler.Core/RedditCrawler.Core.csproj src/RedditCrawler.Core/
COPY src/RedditCrawler/RedditCrawler.csproj src/RedditCrawler/
RUN dotnet restore src/RedditCrawler/RedditCrawler.csproj

COPY . .
RUN dotnet publish src/RedditCrawler/RedditCrawler.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0-preview
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "RedditCrawler.dll"]
