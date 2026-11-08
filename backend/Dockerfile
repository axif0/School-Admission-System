# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for optimal layer caching
COPY ["SchoolAdmission.sln", "./"]
COPY ["src/SchoolAdmission.Api/SchoolAdmission.Api.csproj", "src/SchoolAdmission.Api/"]
COPY ["tests/SchoolAdmission.Tests/SchoolAdmission.Tests.csproj", "tests/SchoolAdmission.Tests/"]

# Restore dependencies
RUN dotnet restore

# Copy all source code after restore
COPY . .

# Build with restored dependencies
RUN dotnet build -c Release --no-restore

# Test stage for running tests inside Docker
FROM build AS test
WORKDIR /src/tests/SchoolAdmission.Tests
ENTRYPOINT ["dotnet", "test", "--no-build", "-c", "Release", "--logger:console;verbosity=normal"]

# Publish stage
FROM build AS publish
WORKDIR /src/src/SchoolAdmission.Api
RUN dotnet publish -c Release -o /app/publish --no-build

# Runtime stage: unprivileged non-root user ($APP_UID)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]
