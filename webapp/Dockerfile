FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY AMPMWeb.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish
# Explicitly copy Views to publish output
RUN cp -r /src/Views /app/publish/Views

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /data

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
ENTRYPOINT ["dotnet", "AMPMWeb.dll"]
