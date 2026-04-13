# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0

RUN apt-get update && apt-get install -y ffmpeg

WORKDIR /app
COPY --from=build /app/out .

# создаём папку для временных видео
RUN mkdir uploads

EXPOSE 8080
ENTRYPOINT ["dotnet", "StoryChain.Api.dll"]