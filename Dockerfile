# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Jadeer.Exam.csproj ./
RUN dotnet restore Jadeer.Exam.csproj
COPY . .
RUN dotnet publish Jadeer.Exam.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
# Do NOT set ASPNETCORE_URLS here — Railway injects PORT and it must win.
EXPOSE 8080
CMD ["sh", "-c", "echo Listening on PORT=${PORT:-8080} && exec dotnet Jadeer.Exam.dll --urls http://0.0.0.0:${PORT:-8080}"]
