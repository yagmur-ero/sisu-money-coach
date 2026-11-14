FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PersonalEconomicCoach.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish PersonalEconomicCoach.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "PersonalEconomicCoach.Api.dll"]
