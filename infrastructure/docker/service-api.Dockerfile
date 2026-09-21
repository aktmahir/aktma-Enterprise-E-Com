FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
ARG PROJECT
ARG DLL
RUN dotnet publish ${PROJECT} -c Release -o /app/publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG DLL
WORKDIR /app
COPY --from=build /app/publish .
ENV APP_DLL=${DLL}
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet /app/${APP_DLL}"]