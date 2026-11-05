FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PrayGamex.Domain/*.csproj PrayGamex.Domain/
COPY src/PrayGamex.Application/*.csproj PrayGamex.Application/
COPY src/PrayGamex.Infrastructure/*.csproj PrayGamex.Infrastructure/
COPY src/PrayGamex.Api/*.csproj PrayGamex.Api/
RUN dotnet restore PrayGamex.Api/PrayGamex.Api.csproj
COPY src/ .
RUN dotnet publish PrayGamex.Api/PrayGamex.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PrayGamex.Api.dll"]