FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY AMPMWeb.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/Data/ampm.db /data/ampm.db
RUN mkdir -p /data

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DB_PATH=/data/ampm.db

EXPOSE 8080
ENTRYPOINT ["dotnet", "AMPMWeb.dll"]
